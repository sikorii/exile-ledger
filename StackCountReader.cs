using System.Drawing.Imaging;
using System.Globalization;
using System.Text.RegularExpressions;
using Tesseract;

namespace Poe2PriceChecker;

internal static class StackCountReader
{
    public static QuantityReadResult ReadQuantity(Bitmap screenshot, Rectangle slotBounds, string tessData, StackCountReadOptions? options = null)
    {
        options ??= StackCountReadOptions.Default;

        var attempts = new List<QuantityOcrAttempt>();
        foreach (var region in BuildCountRegions(slotBounds, options.CoordinateScale))
        {
            using var crop = screenshot.Clone(region, screenshot.PixelFormat);
            using var processed = PrepareCountForOcr(crop);
            options.SaveDebugCrop(crop, processed, region.Width);

            var template = CountDigitTemplateMatcher.ReadDigits(processed);
            attempts.Add(new QuantityOcrAttempt(
                region,
                template.Digits,
                template.Confidence,
                DigitsOnly(ReadWholeDigits(processed, tessData)),
                DigitsOnly(ReadSplitDigits(processed, tessData))));

            using var strictProcessed = PrepareCountForOcr(crop, strict: true);
            var strictTemplate = CountDigitTemplateMatcher.ReadDigits(strictProcessed);
            if (strictTemplate.Digits.Length > 0)
            {
                attempts.Add(new QuantityOcrAttempt(
                    region,
                    strictTemplate.Digits,
                    strictTemplate.Confidence,
                    string.Empty,
                    string.Empty,
                    "strict"));
            }
        }

        var chosen = ChooseQuantityDigits(attempts, options);
        var recoveryText = string.Empty;
        if (!options.IsRuneMode && chosen.Digits?.Length == 1)
        {
            var imageRecovery = TryRecoverCurrencyMultiDigitFromImage(screenshot, slotBounds, chosen.Digits, options.CoordinateScale);
            recoveryText += imageRecovery.DebugText;
            if (imageRecovery.Choice is not null)
            {
                chosen = imageRecovery.Choice;
            }
            else
            {
                var rawRecovery = TryRecoverCurrencyMultiDigitFromRaw(screenshot, slotBounds, tessData, chosen.Digits, options.CoordinateScale);
                recoveryText += rawRecovery.DebugText;
                if (rawRecovery.Choice is not null)
                {
                    chosen = rawRecovery.Choice;
                }
                else if (rawRecovery.HasConflict)
                {
                    chosen = chosen with
                    {
                        Confidence = Math.Min(chosen.Confidence, 0.45),
                        Method = chosen.Method + "+raw-conflict"
                    };
                }
            }
        }

        if (ShouldTryCompactCountRecovery(chosen, attempts, options))
        {
            var compactRecovery = TryReadCompactQuantityFromRaw(screenshot, slotBounds, tessData, options.CoordinateScale);
            if (compactRecovery.Choice is not null)
            {
                chosen = compactRecovery.Choice;
                recoveryText += compactRecovery.DebugText;
            }
            else if (compactRecovery.DebugText.Length > 0)
            {
                recoveryText += compactRecovery.DebugText;
            }

            if (compactRecovery.Choice is null)
            {
                var visualCompactRecovery = TryReadCompactQuantityFromImage(screenshot, slotBounds, chosen.Digits, options.CoordinateScale);
                if (visualCompactRecovery.Choice is not null)
                {
                    chosen = visualCompactRecovery.Choice;
                    recoveryText += visualCompactRecovery.DebugText;
                }
                else if (visualCompactRecovery.DebugText.Length > 0)
                {
                    recoveryText += visualCompactRecovery.DebugText;
                }
            }
        }

        var quantity = int.TryParse(chosen.Digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(1, parsed)
            : 1;
        CountCropTrainingStore.TrySaveDebugCrop(
            screenshot,
            slotBounds,
            options.Mode ?? "unknown",
            options.SlotIndex ?? -1,
            quantity,
            chosen.Method,
            options.ScanId,
            options.DebugDirectory);

        var debugText = string.Join(
            "; ",
            attempts.Select(attempt =>
                $"w{attempt.Region.Width}{(attempt.Variant.Length == 0 ? string.Empty : "-" + attempt.Variant)}:template='{attempt.TemplateDigits}' c={attempt.TemplateConfidence:0.00} whole='{attempt.WholeDigits}' split='{attempt.SplitDigits}'"));

        return new QuantityReadResult(
            quantity,
            $"{debugText}{recoveryText}; method={chosen.Method}",
            chosen.Confidence,
            chosen.Method);
    }

    public static QuantityReadResult ReadRuneQuantity(Bitmap screenshot, Rectangle slotBounds, string tessData, StackCountReadOptions? options = null)
    {
        options ??= StackCountReadOptions.Default with { Mode = "runes" };
        if (!options.IsRuneMode)
        {
            options = options with { Mode = "runes" };
        }

        return ReadQuantity(screenshot, slotBounds, tessData, options);
    }

    public static DigitTrainingSaveResult SaveTrainingSamplesFromOverride(
        Bitmap stashCrop,
        Rectangle slotBounds,
        int quantity,
        string mode,
        int slotIndex,
        string? debugDirectory,
        double coordinateScale = 1.0)
    {
        var digits = quantity.ToString(CultureInfo.InvariantCulture);
        if (digits.Length is < 1 or > 3 || !digits.All(char.IsDigit))
        {
            return new DigitTrainingSaveResult(0, $"x{quantity} is outside the current 1-3 digit training range.");
        }

        var candidates = new List<DigitTrainingRegionCandidate>();
        foreach (var region in BuildCountRegions(slotBounds, coordinateScale))
        {
            if (!ContainsRectangle(stashCrop.Size, region))
            {
                continue;
            }

            using var crop = stashCrop.Clone(region, stashCrop.PixelFormat);
            using var processed = PrepareCountForOcr(crop);
            var groups = CountDigitTemplateMatcher.FindDigitComponentsForTraining(processed);
            candidates.Add(new DigitTrainingRegionCandidate(region, false, CloneRectangles(groups)));

            using var strictProcessed = PrepareCountForOcr(crop, strict: true);
            var strictGroups = CountDigitTemplateMatcher.FindDigitComponentsForTraining(strictProcessed);
            candidates.Add(new DigitTrainingRegionCandidate(region, true, CloneRectangles(strictGroups)));
        }

        var expectedWidth = digits.Length switch
        {
            1 => ScaleLength(44, coordinateScale),
            2 => ScaleLength(70, coordinateScale),
            _ => ScaleLength(102, coordinateScale)
        };

        var selected = candidates
            .Where(candidate => candidate.Groups.Count == digits.Length)
            .OrderBy(candidate => candidate.Strict)
            .ThenBy(candidate => Math.Abs(candidate.Region.Width - expectedWidth))
            .FirstOrDefault();
        if (selected is null)
        {
            SaveTrainingFailureDebug(stashCrop, slotBounds, mode, slotIndex, debugDirectory);
            return new DigitTrainingSaveResult(0, $"No clean {digits.Length}-digit training crop found for slot {slotIndex}.");
        }

        using var selectedCrop = stashCrop.Clone(selected.Region, stashCrop.PixelFormat);
        using var selectedProcessed = PrepareCountForOcr(selectedCrop, selected.Strict);
        SaveTrainingDebugImages(selectedCrop, selectedProcessed, mode, slotIndex, quantity, selected.Strict, debugDirectory);

        var store = DigitTrainingStore.CreateDefault();
        var saved = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            using var digitCrop = selectedProcessed.Clone(selected.Groups[i], PixelFormat.Format24bppRgb);
            if (store.SaveSample(digits[i], digitCrop, $"{mode}:slot-{slotIndex}:x{quantity}:pos-{i}"))
            {
                saved++;
            }
        }

        if (saved > 0)
        {
            CountDigitTemplateMatcher.InvalidateTemplates();
        }

        return saved > 0
            ? new DigitTrainingSaveResult(saved, $"saved {saved} digit sample{(saved == 1 ? string.Empty : "s")}")
            : new DigitTrainingSaveResult(0, $"training samples already existed for slot {slotIndex} x{quantity}.");
    }

    internal static IEnumerable<Rectangle> BuildCountRegions(Rectangle slotBounds, double coordinateScale = 1.0)
    {
        var scale = coordinateScale <= 0 ? 1.0 : coordinateScale;
        var x = slotBounds.X + ScaleLength(6, scale);
        var y = slotBounds.Y;
        var right = slotBounds.Right - ScaleLength(4, scale);
        foreach (var desiredWidth in new[] { 44, 70, 102 })
        {
            var width = Math.Max(1, Math.Min(ScaleLength(desiredWidth, scale), right - x));
            yield return new Rectangle(x, y, width, ScaleLength(40, scale));
        }
    }

    private static int ScaleLength(int baseValue, double scale)
    {
        return Math.Max(1, (int)Math.Round(baseValue * scale, MidpointRounding.AwayFromZero));
    }

    private static bool ContainsRectangle(Size size, Rectangle rectangle)
    {
        return rectangle.X >= 0 &&
            rectangle.Y >= 0 &&
            rectangle.Width > 0 &&
            rectangle.Height > 0 &&
            rectangle.Right <= size.Width &&
            rectangle.Bottom <= size.Height;
    }

    private static IReadOnlyList<Rectangle> CloneRectangles(IReadOnlyList<Rectangle> rectangles)
    {
        return rectangles.ToArray();
    }

    private static QuantityChoice ChooseQuantityDigits(IReadOnlyList<QuantityOcrAttempt> attempts, StackCountReadOptions options)
    {
        if (attempts.Count == 0)
        {
            return new QuantityChoice(null, 0, "default");
        }

        if (options.IsRuneMode && TryChooseRuneQuantity(attempts, out var runeChoice))
        {
            return runeChoice;
        }

        if (TryChooseCurrencyQuantity(attempts, out var currencyChoice))
        {
            return currencyChoice;
        }

        var confidentTemplate = attempts
            .Where(attempt => attempt.TemplateDigits.Length > 0 && attempt.TemplateConfidence >= 0.72)
            .OrderByDescending(attempt => attempt.TemplateDigits.Length)
            .ThenByDescending(attempt => attempt.TemplateConfidence)
            .ThenByDescending(attempt => attempt.Region.Width)
            .FirstOrDefault();
        if (confidentTemplate is not null)
        {
            return new QuantityChoice(confidentTemplate.TemplateDigits, confidentTemplate.TemplateConfidence, "template");
        }

        var regularScores = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var attempt in attempts)
        {
            var bestOcrLength = Math.Max(attempt.SplitDigits.Length, attempt.WholeDigits.Length);
            if (attempt.TemplateDigits.Length >= bestOcrLength)
            {
                AddScore(regularScores, attempt.TemplateDigits, (int)Math.Round(attempt.TemplateConfidence * 10) + attempt.TemplateDigits.Length * 5 + (attempt.Region.Width == 70 ? 2 : 0));
            }

            if (attempt.SplitDigits.Length == 1 &&
                attempt.TemplateDigits.Length == 1 &&
                attempt.SplitDigits != attempt.TemplateDigits)
            {
                AddScore(regularScores, attempt.SplitDigits + attempt.TemplateDigits, 10 + (attempt.Region.Width == 70 ? 2 : 0));
            }

            AddScore(regularScores, attempt.SplitDigits, attempt.SplitDigits.Length * 3 + (attempt.Region.Width == 70 ? 1 : 0));
            AddScore(regularScores, attempt.WholeDigits, attempt.WholeDigits.Length);
        }

        var mediumAttempt = attempts.FirstOrDefault(attempt => attempt.Region.Width == 70);
        if (mediumAttempt is not null && mediumAttempt.WholeDigits.Length is >= 2 and <= 3)
        {
            var shorterSplitConfirmsPrefix = attempts.Any(attempt =>
                attempt.SplitDigits.Length == mediumAttempt.WholeDigits.Length - 1 &&
                mediumAttempt.WholeDigits.StartsWith(attempt.SplitDigits, StringComparison.Ordinal));
            if (shorterSplitConfirmsPrefix)
            {
                return BestCandidate(regularScores);
            }

            var singleDigitSplits = attempts
                .Select(attempt => attempt.SplitDigits)
                .Where(digits => digits.Length == 1)
                .Distinct(StringComparer.Ordinal)
                .Count();

            var wideAttempt = attempts.FirstOrDefault(attempt => attempt.Region.Width == 102);
            var wideWholeConfirmsShort = wideAttempt?.WholeDigits == mediumAttempt.WholeDigits[..1];

            if ((!wideWholeConfirmsShort || singleDigitSplits >= 2) &&
                (attempts.Any(attempt => mediumAttempt.WholeDigits.StartsWith(attempt.SplitDigits, StringComparison.Ordinal) && attempt.SplitDigits.Length == 1) ||
                 singleDigitSplits >= 2))
            {
                AddScore(regularScores, mediumAttempt.WholeDigits, mediumAttempt.WholeDigits.Length * 4);
            }
        }

        return BestCandidate(regularScores);

        static bool TryChooseCurrencyQuantity(IReadOnlyList<QuantityOcrAttempt> attempts, out QuantityChoice choice)
        {
            var exactOcr = BestGroupedCandidate(
                attempts
                    .Where(attempt => IsUsefulMultiDigit(attempt.WholeDigits) && attempt.WholeDigits == attempt.SplitDigits)
                    .Select(attempt => new CandidateDigits(attempt.WholeDigits, 0.74 + (attempt.Region.Width == 70 ? 0.03 : 0), "ocr-exact")),
                requireRepeated: false);
            if (exactOcr is not null)
            {
                choice = exactOcr;
                return true;
            }

            var longTemplate = attempts
                .Where(attempt => IsUsefulMultiDigit(attempt.TemplateDigits) && attempt.TemplateConfidence >= 0.78)
                .OrderByDescending(attempt => attempt.TemplateDigits.Length)
                .ThenByDescending(attempt => attempt.TemplateConfidence)
                .ThenBy(attempt => Math.Abs(attempt.Region.Width - 70))
                .FirstOrDefault();
            if (longTemplate is not null)
            {
                choice = new QuantityChoice(longTemplate.TemplateDigits, longTemplate.TemplateConfidence, "template-long");
                return true;
            }

            var guardedSingle = TryChooseCurrencySingleOverOcrJunk(attempts);
            if (guardedSingle is not null)
            {
                choice = guardedSingle;
                return true;
            }

            var repeatedOcr = BestGroupedCandidate(
                attempts.SelectMany(attempt => new[]
                    {
                        new CandidateDigits(attempt.WholeDigits, 0.66, "ocr-repeat"),
                        new CandidateDigits(attempt.SplitDigits, 0.62, "ocr-repeat")
                    })
                    .Where(candidate => IsUsefulMultiDigit(candidate.Digits)),
                requireRepeated: true);
            if (repeatedOcr is not null)
            {
                choice = repeatedOcr;
                return true;
            }

            choice = default!;
            return false;
        }

        static QuantityChoice? TryChooseCurrencySingleOverOcrJunk(IReadOnlyList<QuantityOcrAttempt> attempts)
        {
            var strongSingle = attempts
                .Where(attempt => attempt.TemplateDigits.Length == 1)
                .GroupBy(attempt => attempt.TemplateDigits, StringComparer.Ordinal)
                .Select(group => new
                {
                    Digit = group.Key,
                    Count = group.Count(attempt => attempt.TemplateConfidence >= 0.94),
                    Confidence = group.Max(attempt => attempt.TemplateConfidence),
                    HasStrictConfirm = group.Any(attempt => attempt.Variant == "strict" && attempt.TemplateConfidence >= 0.78)
                })
                .Where(group => group.Count >= 2 && group.HasStrictConfirm)
                .OrderByDescending(group => group.Confidence)
                .FirstOrDefault();

            if (strongSingle is null)
            {
                return null;
            }

            var splitDisagrees = attempts.Any(attempt =>
                attempt.SplitDigits.Length > 0 &&
                attempt.SplitDigits != strongSingle.Digit &&
                !attempt.SplitDigits.StartsWith(strongSingle.Digit, StringComparison.Ordinal));
            if (splitDisagrees)
            {
                return null;
            }

            var templateDisagrees = attempts.Any(attempt =>
                attempt.TemplateDigits.Length >= 2 &&
                attempt.TemplateConfidence >= 0.78 &&
                !attempt.TemplateDigits.StartsWith(strongSingle.Digit, StringComparison.Ordinal));
            if (templateDisagrees)
            {
                return null;
            }

            var repeatedWholeJunk = attempts
                .Select(attempt => attempt.WholeDigits)
                .Where(digits =>
                    digits.Length is >= 2 and <= 3 &&
                    digits.StartsWith(strongSingle.Digit, StringComparison.Ordinal) &&
                    !digits.All(ch => ch == strongSingle.Digit[0]))
                .GroupBy(digits => digits, StringComparer.Ordinal)
                .Any(group => group.Count() >= 2);
            if (!repeatedWholeJunk)
            {
                return null;
            }

            var corroboratedMulti = attempts.Any(attempt =>
                attempt.TemplateDigits.Length >= 2 &&
                attempt.TemplateConfidence >= 0.78 &&
                attempt.TemplateDigits.StartsWith(strongSingle.Digit, StringComparison.Ordinal)) ||
                attempts
                    .Select(attempt => attempt.SplitDigits)
                    .Where(digits => digits.Length is >= 2 and <= 3 && digits.StartsWith(strongSingle.Digit, StringComparison.Ordinal))
                    .GroupBy(digits => digits, StringComparer.Ordinal)
                    .Any(group => group.Count() >= 2);
            if (corroboratedMulti)
            {
                return null;
            }

            return new QuantityChoice(
                strongSingle.Digit,
                Math.Min(0.88, strongSingle.Confidence),
                "template-single-over-ocr-junk");
        }

        static bool TryChooseRuneQuantity(IReadOnlyList<QuantityOcrAttempt> attempts, out QuantityChoice choice)
        {
            var singleTemplate = BestGroupedCandidate(
                attempts
                    .Where(attempt => attempt.TemplateDigits.Length == 1 && attempt.TemplateConfidence >= 0.78)
                    .Select(attempt => new CandidateDigits(attempt.TemplateDigits, attempt.TemplateConfidence, "rune-template-single")),
                requireRepeated: false);

            var strongMultiTemplate = attempts
                .Where(attempt => IsUsefulMultiDigit(attempt.TemplateDigits) && attempt.TemplateConfidence >= 0.90)
                .OrderByDescending(attempt => attempt.TemplateDigits.Length)
                .ThenByDescending(attempt => attempt.TemplateConfidence)
                .FirstOrDefault();

            if (singleTemplate is not null &&
                (strongMultiTemplate is null || singleTemplate.Confidence >= strongMultiTemplate.TemplateConfidence - 0.04))
            {
                choice = singleTemplate;
                return true;
            }

            var repeatedSingleOcr = BestGroupedCandidate(
                attempts.SelectMany(attempt => new[]
                    {
                        new CandidateDigits(attempt.SplitDigits, 0.70, "rune-ocr-single"),
                        new CandidateDigits(attempt.WholeDigits, 0.66, "rune-ocr-single")
                    })
                    .Where(candidate => candidate.Digits.Length == 1),
                requireRepeated: true);
            if (repeatedSingleOcr is not null &&
                (strongMultiTemplate is null || repeatedSingleOcr.Confidence >= strongMultiTemplate.TemplateConfidence - 0.10))
            {
                choice = repeatedSingleOcr;
                return true;
            }

            if (strongMultiTemplate is not null)
            {
                choice = new QuantityChoice(strongMultiTemplate.TemplateDigits, strongMultiTemplate.TemplateConfidence, "rune-template-multi");
                return true;
            }

            var repeatedMultiOcr = BestGroupedCandidate(
                attempts.SelectMany(attempt => new[]
                    {
                        new CandidateDigits(attempt.WholeDigits, 0.52, "rune-ocr-multi"),
                        new CandidateDigits(attempt.SplitDigits, 0.48, "rune-ocr-multi")
                    })
                    .Where(candidate => candidate.Digits.Length == 2),
                requireRepeated: true);
            if (repeatedMultiOcr is not null)
            {
                choice = repeatedMultiOcr;
                return true;
            }

            choice = new QuantityChoice(null, 0.20, "rune-default");
            return true;
        }

        static bool IsUsefulMultiDigit(string digits)
        {
            return digits.Length is >= 2 and <= 3;
        }

        static QuantityChoice? BestGroupedCandidate(IEnumerable<CandidateDigits> candidates, bool requireRepeated)
        {
            var groups = candidates
                .Where(candidate => candidate.Digits.Length > 0)
                .GroupBy(candidate => candidate.Digits, StringComparer.Ordinal)
                .Select(group => new
                {
                    Digits = group.Key,
                    Count = group.Count(),
                    Confidence = group.Max(candidate => candidate.Confidence),
                    Method = group.OrderByDescending(candidate => candidate.Confidence).First().Method
                })
                .Where(group => !requireRepeated || group.Count >= 2)
                .OrderByDescending(group => group.Count)
                .ThenByDescending(group => group.Digits.Length)
                .ThenByDescending(group => group.Confidence)
                .FirstOrDefault();

            return groups is null
                ? null
                : new QuantityChoice(groups.Digits, Math.Min(0.86, groups.Confidence + Math.Max(0, groups.Count - 1) * 0.04), groups.Method);
        }

        static QuantityChoice BestCandidate(Dictionary<string, int> scores)
        {
            var best = scores
                .OrderByDescending(pair => pair.Value)
                .ThenByDescending(pair => pair.Key.Length)
                .ThenBy(pair => pair.Key)
                .FirstOrDefault();

            return string.IsNullOrEmpty(best.Key)
                ? new QuantityChoice(null, 0, "default")
                : new QuantityChoice(best.Key, Math.Min(0.64, best.Value / 30d), "mixed");
        }

        static void AddScore(Dictionary<string, int> scores, string digits, int score)
        {
            if (digits.Length == 0 || score <= 0)
            {
                return;
            }

            scores[digits] = scores.GetValueOrDefault(digits) + score;
        }
    }

    internal static Bitmap PrepareCountForOcr(Bitmap input, bool strict = false)
    {
        var output = new Bitmap(input.Width * 4, input.Height * 4, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(output);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(input, 0, 0, output.Width, output.Height);

        for (var y = 0; y < output.Height; y++)
        {
            for (var x = 0; x < output.Width; x++)
            {
                var c = output.GetPixel(x, y);
                var max = Math.Max(c.R, Math.Max(c.G, c.B));
                var min = Math.Min(c.R, Math.Min(c.G, c.B));
                var bright = strict
                    ? c.R > 178 && c.G > 178 && c.B > 178 && max - min < 55
                    : c.R > 135 && c.G > 135 && c.B > 135 && max - min < 80;
                output.SetPixel(x, y, bright ? Color.Black : Color.White);
            }
        }

        return output;
    }

    private static RawRecoveryResult TryRecoverCurrencyMultiDigitFromRaw(
        Bitmap screenshot,
        Rectangle slotBounds,
        string tessData,
        string chosenSingleDigit,
        double coordinateScale)
    {
        var rawDigits = new List<string>();
        foreach (var region in BuildCountRegions(slotBounds, coordinateScale))
        {
            using var crop = screenshot.Clone(region, screenshot.PixelFormat);
            rawDigits.Add(DigitsOnly(ReadRawWholeDigits(crop, tessData)));
        }

        var debugText = "; raw='" + string.Join(",", rawDigits) + "'";
        var recovered = rawDigits
            .Where(digits => digits.Length is >= 2 and <= 3)
            .Where(digits => digits.StartsWith(chosenSingleDigit, StringComparison.Ordinal))
            .Where(digits => !digits.All(ch => ch == chosenSingleDigit[0]))
            .Where(digits => !digits.EndsWith("0", StringComparison.Ordinal))
            .GroupBy(digits => digits, StringComparer.Ordinal)
            .Where(group => group.Count() >= 2)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key.Length)
            .FirstOrDefault();

        var hasConflict = rawDigits.Any(digits =>
            digits.Length is >= 2 and <= 3 &&
            !digits.StartsWith(chosenSingleDigit, StringComparison.Ordinal));

        return recovered is null
            ? new RawRecoveryResult(null, debugText, hasConflict)
            : new RawRecoveryResult(new QuantityChoice(recovered.Key, 0.67, "raw-ocr-confirmed"), debugText, hasConflict);
    }

    private static bool ShouldTryCompactCountRecovery(
        QuantityChoice chosen,
        IReadOnlyList<QuantityOcrAttempt> attempts,
        StackCountReadOptions options)
    {
        if (options.IsRuneMode)
        {
            return false;
        }

        if (!int.TryParse(chosen.Digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity))
        {
            return false;
        }

        if (quantity >= 100)
        {
            return true;
        }

        if (options.Mode?.Contains("expedition", StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }

        return quantity >= 10 &&
            attempts.Any(attempt =>
                attempt.TemplateDigits.Length >= 2 ||
                attempt.WholeDigits.Length >= 2 ||
                attempt.SplitDigits.Length >= 2);
    }

    private static RawRecoveryResult TryReadCompactQuantityFromRaw(
        Bitmap screenshot,
        Rectangle slotBounds,
        string tessData,
        double coordinateScale)
    {
        var rawTexts = new List<string>();
        foreach (var region in BuildCountRegions(slotBounds, coordinateScale))
        {
            if (!ContainsRectangle(screenshot.Size, region))
            {
                continue;
            }

            using var crop = screenshot.Clone(region, screenshot.PixelFormat);
            var text = ReadRawWholeCountText(crop, tessData);
            rawTexts.Add(SquashOcrText(text));
        }

        if (rawTexts.Count == 0 || rawTexts.All(string.IsNullOrWhiteSpace))
        {
            return new RawRecoveryResult(null, string.Empty, false);
        }

        var compactCandidates = rawTexts
            .Select(TryParseCompactCount)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!.Value)
            .ToArray();
        var debugText = "; compact='" + string.Join(",", rawTexts) + "'";
        if (compactCandidates.Length == 0)
        {
            return new RawRecoveryResult(null, debugText, false);
        }

        var winner = compactCandidates
            .GroupBy(candidate => candidate.Quantity)
            .Select(group => new
            {
                Quantity = group.Key,
                Count = group.Count(),
                BestText = group
                    .OrderByDescending(candidate => candidate.Text.Length)
                    .First().Text
            })
            .OrderByDescending(group => group.Count)
            .ThenByDescending(group => group.Quantity)
            .First();

        return new RawRecoveryResult(
            new QuantityChoice(winner.Quantity.ToString(CultureInfo.InvariantCulture), 0.88, "compact-k-count"),
            debugText + $"=>{winner.Quantity}",
            false);
    }

    private static RawRecoveryResult TryReadCompactQuantityFromImage(
        Bitmap screenshot,
        Rectangle slotBounds,
        string? chosenDigits,
        double coordinateScale)
    {
        if (string.IsNullOrWhiteSpace(chosenDigits) ||
            !int.TryParse(chosenDigits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var baseQuantity) ||
            baseQuantity < 10)
        {
            return new RawRecoveryResult(null, string.Empty, false);
        }

        var debug = new List<string>();
        foreach (var region in BuildCountRegions(slotBounds, coordinateScale))
        {
            if (!ContainsRectangle(screenshot.Size, region))
            {
                continue;
            }

            using var crop = screenshot.Clone(region, screenshot.PixelFormat);
            using var processed = PrepareCountForOcr(crop);
            var looksLikeK = LooksLikeCompactKSuffix(processed, chosenDigits.Length, out var suffixDebug);
            debug.Add($"{region.Width}:{(looksLikeK ? "k" : "-")}({suffixDebug})");
            if (looksLikeK)
            {
                var quantity = baseQuantity * 1000;
                return new RawRecoveryResult(
                    new QuantityChoice(quantity.ToString(CultureInfo.InvariantCulture), 0.82, "visual-k-count"),
                    "; visual-k='" + string.Join(",", debug) + $"=>{quantity}'",
                    false);
            }
        }

        return debug.Count == 0
            ? new RawRecoveryResult(null, string.Empty, false)
            : new RawRecoveryResult(null, "; visual-k='" + string.Join(",", debug) + "'", false);
    }

    private static bool LooksLikeCompactKSuffix(Bitmap processed, int digitCount, out string debug)
    {
        if (LooksLikeCompactKZone(processed, digitCount, out debug))
        {
            return true;
        }

        var groups = FindDigitGroups(processed)
            .OrderBy(group => group.X)
            .ToArray();
        if (digitCount < 1 || groups.Length <= digitCount)
        {
            debug = $"{debug};g={groups.Length}";
            return false;
        }

        var suffix = groups[digitCount];
        if (suffix.Width is < 28 or > 86 ||
            suffix.Height is < 58 or > 130 ||
            suffix.Left < processed.Width * 0.36)
        {
            debug = $"{debug};g={groups.Length};r={suffix.X},{suffix.Y},{suffix.Width},{suffix.Height}";
            return false;
        }

        var bandWidth = Math.Max(4, suffix.Width / 4);
        var leftBand = CountInkPixels(processed, new Rectangle(
            suffix.Left,
            suffix.Top,
            bandWidth,
            suffix.Height));
        var upperRight = CountInkPixels(processed, new Rectangle(
            suffix.Left + suffix.Width / 2,
            suffix.Top,
            Math.Max(1, suffix.Width / 2),
            Math.Max(1, suffix.Height / 2)));
        var lowerRight = CountInkPixels(processed, new Rectangle(
            suffix.Left + suffix.Width / 2,
            suffix.Top + suffix.Height / 2,
            Math.Max(1, suffix.Width / 2),
            Math.Max(1, suffix.Height / 2)));
        var middle = CountInkPixels(processed, new Rectangle(
            suffix.Left + suffix.Width / 3,
            suffix.Top + suffix.Height / 3,
            Math.Max(1, suffix.Width / 3),
            Math.Max(1, suffix.Height / 3)));

        var componentLooksLikeK = leftBand >= 80 &&
            upperRight >= 70 &&
            lowerRight >= 70 &&
            middle >= 35;
        debug = $"{debug};g={groups.Length};r={suffix.X},{suffix.Y},{suffix.Width},{suffix.Height};c={leftBand}/{upperRight}/{lowerRight}/{middle}";
        return componentLooksLikeK;
    }

    private static bool LooksLikeCompactKZone(Bitmap processed, int digitCount, out string debug)
    {
        debug = string.Empty;
        if (digitCount != 2 || processed.Width < 220 || processed.Height < 120)
        {
            debug = $"z=skip-{digitCount}-{processed.Width}x{processed.Height}";
            return false;
        }

        var left = (int)Math.Round(processed.Width * 0.45);
        var right = (int)Math.Round(processed.Width * 0.86);
        var top = Math.Min(processed.Height - 1, 20);
        var bottom = Math.Min(processed.Height, 135);
        if (right <= left || bottom <= top)
        {
            debug = "z=bad";
            return false;
        }

        var leftBand = CountInkPixels(processed, Rectangle.FromLTRB(
            left,
            top,
            (int)Math.Round(processed.Width * 0.55),
            bottom));
        var upperRight = CountInkPixels(processed, Rectangle.FromLTRB(
            (int)Math.Round(processed.Width * 0.58),
            top,
            right,
            Math.Min(bottom, 75)));
        var lowerRight = CountInkPixels(processed, Rectangle.FromLTRB(
            (int)Math.Round(processed.Width * 0.58),
            Math.Min(bottom - 1, 80),
            right,
            bottom));
        var middle = CountInkPixels(processed, Rectangle.FromLTRB(
            (int)Math.Round(processed.Width * 0.54),
            Math.Min(bottom - 1, 55),
            (int)Math.Round(processed.Width * 0.72),
            Math.Min(bottom, 105)));

        debug = $"z={leftBand}/{upperRight}/{lowerRight}/{middle}";
        return leftBand >= 350 &&
            upperRight >= 350 &&
            lowerRight >= 850 &&
            middle >= 240;
    }

    private static int CountInkPixels(Bitmap processed, Rectangle region)
    {
        var safe = Rectangle.Intersect(new Rectangle(Point.Empty, processed.Size), region);
        var count = 0;
        for (var y = safe.Top; y < safe.Bottom; y++)
        {
            for (var x = safe.Left; x < safe.Right; x++)
            {
                var c = processed.GetPixel(x, y);
                if (c.R < 80 && c.G < 80 && c.B < 80)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static RawRecoveryResult TryRecoverCurrencyMultiDigitFromImage(
        Bitmap screenshot,
        Rectangle slotBounds,
        string chosenSingleDigit,
        double coordinateScale)
    {
        if (chosenSingleDigit != "1")
        {
            return new RawRecoveryResult(null, string.Empty, false);
        }

        var candidates = new List<TemplateDigitReadResult>();
        foreach (var region in BuildCountRegions(slotBounds, coordinateScale))
        {
            if (!ContainsRectangle(screenshot.Size, region))
            {
                continue;
            }

            using var crop = screenshot.Clone(region, screenshot.PixelFormat);
            using var processed = PrepareCountForOcr(crop);
            candidates.Add(CountDigitTemplateMatcher.ReadLeftCountDigits(processed));
        }

        var debugText = "; image='" + string.Join(",", candidates.Select(candidate =>
            candidate.Digits.Length == 0 ? string.Empty : $"{candidate.Digits}:{candidate.Confidence:0.00}")) + "'";
        var recovered = candidates
            .Where(candidate => candidate.Digits.Length is >= 2 and <= 3)
            .Where(candidate => candidate.Digits.StartsWith(chosenSingleDigit, StringComparison.Ordinal))
            .GroupBy(candidate => candidate.Digits, StringComparer.Ordinal)
            .Select(group => new
            {
                Digits = group.Key,
                Count = group.Count(),
                Confidence = group.Max(candidate => candidate.Confidence)
            })
            .Where(group => group.Count >= 2 || group.Confidence >= 0.76)
            .OrderByDescending(group => group.Count)
            .ThenByDescending(group => group.Confidence)
            .FirstOrDefault();

        return recovered is null
            ? new RawRecoveryResult(null, debugText, false)
            : new RawRecoveryResult(
                new QuantityChoice(recovered.Digits, Math.Min(0.70, recovered.Confidence), "image-left-count"),
                debugText,
                false);
    }

    private static string ReadRawWholeDigits(Bitmap crop, string tessDataDirectory)
    {
        using var enlarged = new Bitmap(crop.Width * 4, crop.Height * 4, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(enlarged))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(crop, 0, 0, enlarged.Width, enlarged.Height);
        }

        using var padded = AddPadding(enlarged, 18);
        var path = Path.Combine(Path.GetTempPath(), $"poe2-count-raw-{Guid.NewGuid():N}.png");
        try
        {
            CurrencyScanner.SaveBitmap(padded, path);
            return RunDigitOcr(path, tessDataDirectory);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string ReadRawWholeCountText(Bitmap crop, string tessDataDirectory)
    {
        using var enlarged = new Bitmap(crop.Width * 4, crop.Height * 4, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(enlarged))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(crop, 0, 0, enlarged.Width, enlarged.Height);
        }

        using var padded = AddPadding(enlarged, 18);
        var path = Path.Combine(Path.GetTempPath(), $"poe2-count-compact-{Guid.NewGuid():N}.png");
        try
        {
            CurrencyScanner.SaveBitmap(padded, path);
            return RunCountOcr(path, tessDataDirectory);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string RunDigitOcr(string imagePath, string tessDataDirectory)
    {
        using var engine = new TesseractEngine(tessDataDirectory, "eng", EngineMode.Default);
        engine.SetVariable("tessedit_char_whitelist", "0123456789");
        engine.DefaultPageSegMode = PageSegMode.SingleWord;
        using var pix = Pix.LoadFromFile(imagePath);
        using var page = engine.Process(pix);
        return page.GetText();
    }

    private static string RunCountOcr(string imagePath, string tessDataDirectory)
    {
        using var engine = new TesseractEngine(tessDataDirectory, "eng", EngineMode.Default);
        engine.SetVariable("tessedit_char_whitelist", "0123456789Kk.,");
        engine.DefaultPageSegMode = PageSegMode.SingleWord;
        using var pix = Pix.LoadFromFile(imagePath);
        using var page = engine.Process(pix);
        return page.GetText();
    }

    private static string ReadWholeDigits(Bitmap processed, string tessDataDirectory)
    {
        using var padded = AddPadding(processed, 18);
        var path = Path.Combine(Path.GetTempPath(), $"poe2-count-whole-{Guid.NewGuid():N}.png");
        try
        {
            CurrencyScanner.SaveBitmap(padded, path);
            return RunDigitOcr(path, tessDataDirectory);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string ReadSplitDigits(Bitmap processed, string tessDataDirectory)
    {
        var groups = FindDigitGroups(processed);
        if (groups.Count == 0)
        {
            return string.Empty;
        }

        var digits = new List<char>();
        foreach (var group in groups)
        {
            using var digit = processed.Clone(group, PixelFormat.Format24bppRgb);
            using var padded = AddPadding(digit, 16);
            var path = Path.Combine(Path.GetTempPath(), $"poe2-digit-{Guid.NewGuid():N}.png");
            try
            {
                CurrencyScanner.SaveBitmap(padded, path);
                var text = RunSingleDigitOcr(path, tessDataDirectory);
                var match = Regex.Match(text, @"\d");
                if (match.Success)
                {
                    digits.Add(match.Value[0]);
                }
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        return digits.Count > 0 ? new string(digits.ToArray()) : string.Empty;
    }

    private static string DigitsOnly(string text)
    {
        return string.Concat(text.Where(char.IsDigit));
    }

    private static string SquashOcrText(string text)
    {
        return Regex.Replace(text, @"\s+", string.Empty).Trim();
    }

    private static (int Quantity, string Text)? TryParseCompactCount(string text)
    {
        var match = Regex.Match(
            text,
            @"(?<number>\d+(?:[\.,]\d+)?)\s*[Kk]",
            RegexOptions.CultureInvariant);
        var hasSuffix = match.Success;
        if (!match.Success)
        {
            match = Regex.Match(
                text,
                @"(?<number>\d+[\.,]\d+)",
                RegexOptions.CultureInvariant);
        }

        if (!match.Success)
        {
            return null;
        }

        var numberText = match.Groups["number"].Value.Replace(',', '.');
        if (!decimal.TryParse(numberText, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return null;
        }

        var quantity = (int)Math.Round(value * 1000m, MidpointRounding.AwayFromZero);
        return quantity > 0 ? (quantity, hasSuffix ? match.Value : match.Value + "(k implied)") : null;
    }

    private static IReadOnlyList<Rectangle> FindDigitGroups(Bitmap processed)
    {
        var columns = new bool[processed.Width];
        for (var x = 0; x < processed.Width; x++)
        {
            var blackPixels = 0;
            for (var y = 0; y < processed.Height; y++)
            {
                var c = processed.GetPixel(x, y);
                if (c.R < 80 && c.G < 80 && c.B < 80)
                {
                    blackPixels++;
                }
            }

            columns[x] = blackPixels > 2;
        }

        var runs = new List<(int Start, int End)>();
        var start = -1;
        for (var x = 0; x < columns.Length; x++)
        {
            if (columns[x] && start < 0)
            {
                start = x;
            }
            else if (!columns[x] && start >= 0)
            {
                runs.Add((start, x - 1));
                start = -1;
            }
        }

        if (start >= 0)
        {
            runs.Add((start, columns.Length - 1));
        }

        var groups = new List<Rectangle>();
        foreach (var (runStart, runEnd) in runs.Where(run => run.End - run.Start >= 24))
        {
            var top = processed.Height;
            var bottom = 0;
            for (var x = runStart; x <= runEnd; x++)
            {
                for (var y = 0; y < processed.Height; y++)
                {
                    var c = processed.GetPixel(x, y);
                    if (c.R < 80 && c.G < 80 && c.B < 80)
                    {
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
                }
            }

            if (bottom <= top)
            {
                continue;
            }

            var x0 = Math.Max(0, runStart - 3);
            var y0 = Math.Max(0, top - 3);
            var x1 = Math.Min(processed.Width - 1, runEnd + 3);
            var y1 = Math.Min(processed.Height - 1, bottom + 3);
            var rect = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
            if (rect.Width >= 12 && rect.Height >= 24)
            {
                groups.Add(rect);
            }
        }

        return groups.Take(3).ToArray();
    }

    private static string RunSingleDigitOcr(string imagePath, string tessDataDirectory)
    {
        using var engine = new TesseractEngine(tessDataDirectory, "eng", EngineMode.Default);
        engine.SetVariable("tessedit_char_whitelist", "0123456789");
        engine.DefaultPageSegMode = PageSegMode.SingleChar;
        using var pix = Pix.LoadFromFile(imagePath);
        using var page = engine.Process(pix);
        return page.GetText();
    }

    private static Bitmap AddPadding(Bitmap input, int padding)
    {
        var output = new Bitmap(input.Width + padding * 2, input.Height + padding * 2, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(output);
        graphics.Clear(Color.White);
        graphics.DrawImage(input, padding, padding, input.Width, input.Height);
        return output;
    }

    private static void SaveTrainingDebugImages(
        Bitmap raw,
        Bitmap processed,
        string mode,
        int slotIndex,
        int quantity,
        bool strict,
        string? debugDirectory)
    {
        if (string.IsNullOrWhiteSpace(debugDirectory))
        {
            return;
        }

        var directory = Path.Combine(debugDirectory, "digit-training", "corrections", mode);
        Directory.CreateDirectory(directory);
        var prefix = $"slot-{slotIndex:00}-x{quantity}-{(strict ? "strict" : "normal")}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}";
        CurrencyScanner.SaveBitmap(raw, Path.Combine(directory, $"{prefix}-raw.png"));
        CurrencyScanner.SaveBitmap(processed, Path.Combine(directory, $"{prefix}-processed.png"));
    }

    private static void SaveTrainingFailureDebug(
        Bitmap stashCrop,
        Rectangle slotBounds,
        string mode,
        int slotIndex,
        string? debugDirectory)
    {
        if (string.IsNullOrWhiteSpace(debugDirectory) || !ContainsRectangle(stashCrop.Size, slotBounds))
        {
            return;
        }

        var directory = Path.Combine(debugDirectory, "digit-training", "corrections", mode);
        Directory.CreateDirectory(directory);
        using var slotCrop = stashCrop.Clone(slotBounds, stashCrop.PixelFormat);
        CurrencyScanner.SaveBitmap(slotCrop, Path.Combine(directory, $"slot-{slotIndex:00}-failed-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.png"));
    }

    private static class CountDigitTemplateMatcher
    {
        private const int TemplateWidth = 28;
        private const int TemplateHeight = 40;
        private static readonly object TemplateLock = new();
        private static IReadOnlyList<DigitTemplate>? _templates;

        public static TemplateDigitReadResult ReadDigits(Bitmap processed)
        {
            var templates = GetTemplates();
            if (templates.Count == 0)
            {
                return TemplateDigitReadResult.Empty;
            }

            var groups = FindTemplateDigitComponents(processed);
            if (groups.Count == 0)
            {
                return TemplateDigitReadResult.Empty;
            }

            var digits = new List<char>();
            var scores = new List<double>();
            foreach (var group in groups)
            {
                using var digit = processed.Clone(group, PixelFormat.Format24bppRgb);
                var match = MatchDigit(digit, templates);
                if (match is null)
                {
                    return TemplateDigitReadResult.Empty;
                }

                digits.Add(match.Digit);
                scores.Add(match.Score);
            }

            var confidence = scores.Count == 0
                ? 0
                : Math.Clamp(1 - scores.Average(), 0, 1);
            return new TemplateDigitReadResult(new string(digits.ToArray()), confidence);
        }

        public static IReadOnlyList<Rectangle> FindDigitComponentsForTraining(Bitmap processed)
        {
            return FindTemplateDigitComponents(processed);
        }

        public static TemplateDigitReadResult ReadLeftCountDigits(Bitmap processed)
        {
            var templates = GetTemplates();
            if (templates.Count == 0)
            {
                return TemplateDigitReadResult.Empty;
            }

            var groups = FindLeftCountDigitComponents(processed);
            if (groups.Count < 2)
            {
                return TemplateDigitReadResult.Empty;
            }

            var digits = new List<char>();
            var scores = new List<double>();
            foreach (var group in groups.Take(2))
            {
                using var digit = processed.Clone(group, PixelFormat.Format24bppRgb);
                var match = MatchDigit(digit, templates, maxScore: 0.50);
                if (match is null)
                {
                    var oneThreeGuess = LooksLikeLeadingOneFour(processed, groups)
                        ? new TemplateDigitReadResult("14", 0.58)
                        : LooksLikeLeadingOneThree(processed, groups)
                        ? new TemplateDigitReadResult("13", 0.54)
                        : TemplateDigitReadResult.Empty;
                    return oneThreeGuess;
                }

                digits.Add(match.Digit);
                scores.Add(match.Score);
            }

            var confidence = scores.Count == 0
                ? 0
                : Math.Clamp(1 - scores.Average(), 0, 1);
            if (digits.Count < 2)
            {
                return TemplateDigitReadResult.Empty;
            }

            if (groups.Count > 2)
            {
                confidence = Math.Min(confidence, 0.70);
            }

            return new TemplateDigitReadResult(new string(digits.ToArray()), confidence);
        }

        private static bool LooksLikeLeadingOneFour(Bitmap processed, IReadOnlyList<Rectangle> groups)
        {
            if (groups.Count < 2)
            {
                return false;
            }

            var first = groups[0];
            var second = groups[1];
            if (first.X is < 35 or > 72 ||
                first.Width is < 24 or > 48 ||
                first.Height is < 58 or > 86)
            {
                return false;
            }

            if (second.X is < 82 or > 124 ||
                second.Width is < 28 or > 78 ||
                second.Height is < 58 or > 104 ||
                second.X - first.Right < 6)
            {
                return false;
            }

            var stemWidth = Math.Max(1, second.Width / 3);
            var leftMiddle = CountBlackPixels(processed, new Rectangle(
                second.Left,
                second.Top + second.Height / 4,
                stemWidth,
                Math.Max(1, second.Height / 2)));
            var rightMiddle = CountBlackPixels(processed, new Rectangle(
                second.Right - stemWidth,
                second.Top + second.Height / 4,
                stemWidth,
                Math.Max(1, second.Height / 2)));
            var middleBand = CountBlackPixels(processed, new Rectangle(
                second.Left,
                second.Top + second.Height / 3,
                second.Width,
                Math.Max(1, second.Height / 4)));

            return leftMiddle >= 10 &&
                rightMiddle >= 10 &&
                middleBand >= 24 &&
                (second.Width >= 40 || leftMiddle + rightMiddle >= 28);
        }

        private static bool LooksLikeLeadingOneThree(Bitmap processed, IReadOnlyList<Rectangle> groups)
        {
            if (groups.Count < 2)
            {
                return false;
            }

            var first = groups[0];
            var second = groups[1];
            if (first.X is < 35 or > 72 ||
                first.Width is < 24 or > 48 ||
                first.Height is < 58 or > 86)
            {
                return false;
            }

            if (second.X is < 88 or > 118 ||
                second.Width is < 28 or > 58 ||
                second.Height is < 58 or > 86 ||
                second.X - first.Right < 8)
            {
                return false;
            }

            var firstLeftInk = CountBlackPixels(processed, new Rectangle(first.Left, first.Top, Math.Max(1, first.Width / 3), first.Height));
            var firstRightInk = CountBlackPixels(processed, new Rectangle(first.Right - Math.Max(1, first.Width / 3), first.Top, Math.Max(1, first.Width / 3), first.Height));
            if (firstRightInk < firstLeftInk)
            {
                return false;
            }

            var secondUpper = CountBlackPixels(processed, new Rectangle(second.Left, second.Top, second.Width, Math.Max(1, second.Height / 3)));
            var secondMiddle = CountBlackPixels(processed, new Rectangle(second.Left, second.Top + second.Height / 3, second.Width, Math.Max(1, second.Height / 3)));
            var secondLower = CountBlackPixels(processed, new Rectangle(second.Left, second.Bottom - Math.Max(1, second.Height / 3), second.Width, Math.Max(1, second.Height / 3)));
            return secondUpper > 12 && secondMiddle > 12 && secondLower > 12;
        }

        private static int CountBlackPixels(Bitmap processed, Rectangle region)
        {
            var safe = Rectangle.Intersect(new Rectangle(Point.Empty, processed.Size), region);
            var count = 0;
            for (var y = safe.Top; y < safe.Bottom; y++)
            {
                for (var x = safe.Left; x < safe.Right; x++)
                {
                    if (IsBlack(processed.GetPixel(x, y)))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        public static void InvalidateTemplates()
        {
            lock (TemplateLock)
            {
                _templates = null;
            }
        }

        private static IReadOnlyList<DigitTemplate> GetTemplates()
        {
            lock (TemplateLock)
            {
                _templates ??= BuildTemplates();
                return _templates;
            }
        }

        private static IReadOnlyList<DigitTemplate> BuildTemplates()
        {
            var templates = new List<DigitTemplate>();
            AddReferenceImageTemplates(templates);
            AddTrainingStoreTemplates(templates);

            return templates
                .GroupBy(template => template.Digit)
                .SelectMany(group => group.Take(12))
                .ToArray();
        }

        private static void AddReferenceImageTemplates(List<DigitTemplate> templates)
        {
            var referencePath = FindTemplateReferencePath();
            if (referencePath is null)
            {
                return;
            }

            try
            {
                using var screenshot = CurrencyScanner.LoadBitmapWithoutFileLock(referencePath);
                foreach (var sample in BuildSamples())
                {
                    if (!ContainsRectangle(screenshot.Size, sample.Region))
                    {
                        continue;
                    }

                    using var crop = screenshot.Clone(sample.Region, screenshot.PixelFormat);
                    using var processed = PrepareCountForOcr(crop);
                    var groups = FindTemplateDigitComponents(processed);
                    if (groups.Count != sample.Digits.Length)
                    {
                        continue;
                    }

                    for (var i = 0; i < sample.Digits.Length; i++)
                    {
                        using var digit = processed.Clone(groups[i], PixelFormat.Format24bppRgb);
                        templates.Add(new DigitTemplate(sample.Digits[i], NormalizeDigit(digit)));
                    }
                }
            }
            catch
            {
            }
        }

        private static void AddTrainingStoreTemplates(List<DigitTemplate> templates)
        {
            var store = DigitTrainingStore.CreateDefault();
            foreach (var sample in store.LoadSamples())
            {
                try
                {
                    using var digit = CurrencyScanner.LoadBitmapWithoutFileLock(store.GetAbsolutePath(sample.RelativePath));
                    templates.Add(new DigitTemplate(sample.Digit, NormalizeDigit(digit)));
                }
                catch
                {
                }
            }
        }

        private static TemplateMatch? MatchDigit(Bitmap digit, IReadOnlyList<DigitTemplate> templates, double maxScore = 0.34)
        {
            var input = NormalizeDigit(digit);
            var best = templates
                .Select(template => new TemplateMatch(template.Digit, Difference(input, template.Mask)))
                .OrderBy(match => match.Score)
                .FirstOrDefault();

            return best is not null && best.Score <= maxScore
                ? best
                : null;
        }

        private static IReadOnlyList<Rectangle> FindTemplateDigitComponents(Bitmap processed)
        {
            var seen = new bool[processed.Width, processed.Height];
            var components = new List<Rectangle>();

            for (var y = 0; y < processed.Height; y++)
            {
                for (var x = 0; x < processed.Width; x++)
                {
                    if (seen[x, y] || !IsBlack(processed.GetPixel(x, y)))
                    {
                        continue;
                    }

                    var queue = new Queue<Point>();
                    var pixels = new List<Point>();
                    queue.Enqueue(new Point(x, y));
                    seen[x, y] = true;

                    while (queue.Count > 0)
                    {
                        var point = queue.Dequeue();
                        pixels.Add(point);

                        for (var ny = point.Y - 1; ny <= point.Y + 1; ny++)
                        {
                            for (var nx = point.X - 1; nx <= point.X + 1; nx++)
                            {
                                if (nx < 0 || ny < 0 || nx >= processed.Width || ny >= processed.Height || seen[nx, ny])
                                {
                                    continue;
                                }

                                seen[nx, ny] = true;
                                if (IsBlack(processed.GetPixel(nx, ny)))
                                {
                                    queue.Enqueue(new Point(nx, ny));
                                }
                            }
                        }
                    }

                    if (pixels.Count < 120)
                    {
                        continue;
                    }

                    var left = pixels.Min(point => point.X);
                    var top = pixels.Min(point => point.Y);
                    var right = pixels.Max(point => point.X) + 1;
                    var bottom = pixels.Max(point => point.Y) + 1;
                    var component = Rectangle.FromLTRB(left, top, right, bottom);

                    if (IsPlausibleDigitComponent(component, processed.Width))
                    {
                        components.Add(component);
                    }
                }
            }

            return components
                .OrderBy(component => component.X)
                .Take(3)
                .ToArray();
        }

        private static IReadOnlyList<Rectangle> FindLeftCountDigitComponents(Bitmap processed)
        {
            foreach (var yStart in new[] { 34, 60, 80 })
            {
                var groups = FindLeftCountDigitComponents(processed, yStart);
                if (groups.Count >= 2)
                {
                    return groups;
                }
            }

            return [];
        }

        private static IReadOnlyList<Rectangle> FindLeftCountDigitComponents(Bitmap processed, int requestedYStart)
        {
            var leftLimit = Math.Min(processed.Width, 132);
            var yStart = Math.Min(requestedYStart, processed.Height - 1);
            var yEnd = Math.Min(processed.Height, 150);
            if (leftLimit < 40 || yEnd <= yStart + 20)
            {
                return [];
            }

            var columns = new bool[leftLimit];
            for (var x = 0; x < leftLimit; x++)
            {
                var blackPixels = 0;
                for (var y = yStart; y < yEnd; y++)
                {
                    if (IsBlack(processed.GetPixel(x, y)))
                    {
                        blackPixels++;
                    }
                }

                columns[x] = blackPixels >= 4;
            }

            var runs = new List<(int Start, int End)>();
            var start = -1;
            for (var x = 0; x < columns.Length; x++)
            {
                if (columns[x] && start < 0)
                {
                    start = x;
                }
                else if (!columns[x] && start >= 0)
                {
                    runs.Add((start, x - 1));
                    start = -1;
                }
            }

            if (start >= 0)
            {
                runs.Add((start, columns.Length - 1));
            }

            var groups = new List<Rectangle>();
            foreach (var (runStart, runEnd) in MergeCloseRuns(runs).Where(run => run.End - run.Start >= 14))
            {
                var top = yEnd;
                var bottom = yStart;
                for (var x = runStart; x <= runEnd; x++)
                {
                    for (var y = yStart; y < yEnd; y++)
                    {
                        if (!IsBlack(processed.GetPixel(x, y)))
                        {
                            continue;
                        }

                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
                }

                if (bottom <= top)
                {
                    continue;
                }

                var rect = Rectangle.FromLTRB(
                    Math.Max(0, runStart - 3),
                    Math.Max(0, top - 3),
                    Math.Min(processed.Width, runEnd + 4),
                    Math.Min(processed.Height, bottom + 4));

                if (rect.Width is >= 14 and <= 76 && rect.Height is >= 42 and <= 118)
                {
                    groups.Add(rect);
                }
            }

            return groups
                .OrderBy(group => group.X)
                .Take(3)
                .ToArray();
        }

        private static IEnumerable<(int Start, int End)> MergeCloseRuns(IReadOnlyList<(int Start, int End)> runs)
        {
            if (runs.Count == 0)
            {
                yield break;
            }

            var current = runs[0];
            for (var i = 1; i < runs.Count; i++)
            {
                var next = runs[i];
                if (next.Start - current.End <= 3)
                {
                    current = (current.Start, next.End);
                    continue;
                }

                yield return current;
                current = next;
            }

            yield return current;
        }

        private static bool IsPlausibleDigitComponent(Rectangle component, int imageWidth)
        {
            return component.Width is >= 18 and <= 80 &&
                component.Height is >= 50 and <= 110 &&
                component.Y <= 96 &&
                component.X <= imageWidth - 45;
        }

        private static bool IsBlack(Color color)
        {
            return color.R < 80 && color.G < 80 && color.B < 80;
        }

        private static bool[] NormalizeDigit(Bitmap digit)
        {
            using var normalized = new Bitmap(TemplateWidth, TemplateHeight, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(normalized))
            {
                graphics.Clear(Color.White);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

                var scale = Math.Min(
                    TemplateWidth / (double)digit.Width,
                    TemplateHeight / (double)digit.Height);
                var width = Math.Max(1, (int)Math.Round(digit.Width * scale));
                var height = Math.Max(1, (int)Math.Round(digit.Height * scale));
                var x = (TemplateWidth - width) / 2;
                var y = (TemplateHeight - height) / 2;
                graphics.DrawImage(digit, x, y, width, height);
            }

            var mask = new bool[TemplateWidth * TemplateHeight];
            for (var y = 0; y < TemplateHeight; y++)
            {
                for (var x = 0; x < TemplateWidth; x++)
                {
                    var c = normalized.GetPixel(x, y);
                    mask[y * TemplateWidth + x] = c.R < 128 && c.G < 128 && c.B < 128;
                }
            }

            return mask;
        }

        private static double Difference(bool[] left, bool[] right)
        {
            var different = 0;
            var active = 0;
            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] || right[i])
                {
                    active++;
                    if (left[i] != right[i])
                    {
                        different++;
                    }
                }
            }

            return active == 0
                ? 1
                : different / (double)active;
        }

        private static string? FindTemplateReferencePath()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "assets", "count-digits-reference.png"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Screenshots", "fix.png")),
                Path.Combine(AppContext.BaseDirectory, "Screenshots", "fix.png"),
                Path.Combine(Directory.GetCurrentDirectory(), "Screenshots", "fix.png")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static IEnumerable<DigitTemplateSample> BuildSamples()
        {
            yield return new DigitTemplateSample("1", new Rectangle(250, 1248, 70, 40));
            yield return new DigitTemplateSample("2", new Rectangle(371, 1248, 70, 40));
            yield return new DigitTemplateSample("3", new Rectangle(493, 1248, 70, 40));
            yield return new DigitTemplateSample("4", new Rectangle(614, 1248, 70, 40));
            yield return new DigitTemplateSample("5", new Rectangle(736, 1248, 70, 40));
            yield return new DigitTemplateSample("6", new Rectangle(857, 1248, 70, 40));
            yield return new DigitTemplateSample("7", new Rectangle(979, 1248, 70, 40));
            yield return new DigitTemplateSample("9", new Rectangle(76, 445, 70, 40));
            yield return new DigitTemplateSample("8", new Rectangle(250, 1370, 70, 40));
            yield return new DigitTemplateSample("10", new Rectangle(371, 1370, 70, 40));
            yield return new DigitTemplateSample("10", new Rectangle(493, 1370, 70, 40));
            yield return new DigitTemplateSample("20", new Rectangle(614, 1370, 70, 40));
            yield return new DigitTemplateSample("100", new Rectangle(736, 1370, 90, 40));
        }

        private static bool ContainsRectangle(Size size, Rectangle rectangle)
        {
            return rectangle.X >= 0 &&
                rectangle.Y >= 0 &&
                rectangle.Right <= size.Width &&
                rectangle.Bottom <= size.Height;
        }

        private sealed record DigitTemplate(char Digit, bool[] Mask);

        private sealed record DigitTemplateSample(string Digits, Rectangle Region);

        private sealed record TemplateMatch(char Digit, double Score);
    }
}

internal sealed record StackCountReadOptions(string? DebugDirectory, string? Mode, int? SlotIndex, string? ScanId = null, double CoordinateScale = 1.0)
{
    public static readonly StackCountReadOptions Default = new(null, null, null);

    public bool IsRuneMode =>
        Mode?.Contains("rune", StringComparison.OrdinalIgnoreCase) == true ||
        Mode?.Contains("kalguuran", StringComparison.OrdinalIgnoreCase) == true;

    public void SaveDebugCrop(Bitmap raw, Bitmap processed, int regionWidth)
    {
        if (!CountCropDebugSettings.SaveCountDebugCrops ||
            string.IsNullOrWhiteSpace(DebugDirectory) ||
            string.IsNullOrWhiteSpace(Mode) ||
            SlotIndex is null)
        {
            return;
        }

        var directory = Path.Combine(DebugDirectory, "digit-training", Mode);
        Directory.CreateDirectory(directory);
        var prefix = $"slot-{SlotIndex:00}-w{regionWidth}";
        CurrencyScanner.SaveBitmap(raw, Path.Combine(directory, $"{prefix}-raw.png"));
        CurrencyScanner.SaveBitmap(processed, Path.Combine(directory, $"{prefix}-processed.png"));
    }
}

internal sealed record QuantityReadResult(int Quantity, string DebugText, double Confidence = 0, string Method = "unknown");

internal sealed record QuantityOcrAttempt(Rectangle Region, string TemplateDigits, double TemplateConfidence, string WholeDigits, string SplitDigits, string Variant = "");

internal sealed record TemplateDigitReadResult(string Digits, double Confidence)
{
    public static readonly TemplateDigitReadResult Empty = new(string.Empty, 0);
}

internal sealed record QuantityChoice(string? Digits, double Confidence, string Method);

internal sealed record CandidateDigits(string Digits, double Confidence, string Method);

internal sealed record RawRecoveryResult(QuantityChoice? Choice, string DebugText, bool HasConflict);

internal sealed record DigitTrainingRegionCandidate(Rectangle Region, bool Strict, IReadOnlyList<Rectangle> Groups);

internal sealed record DigitTrainingSaveResult(int SamplesSaved, string Message);
