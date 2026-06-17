using System.Drawing.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Poe2PriceChecker;

internal sealed class RuneshapingScanner
{
    private const float RuneshapingOcrScale = 2f;
    private const int InlineOverlayLabelHeight = 28;
    private const int InlineOverlayMinLabelWidth = 58;
    private const int InlineOverlayMaxLabelWidth = 170;
    private const string RowRetryPreprocessingMode = "row-upscale-threshold";
    private static readonly Rectangle Crop3840x2160 = new(407, 300, 680, 1080);
    private static readonly Regex RewardLine = new(
        @"(?<qty>\d+)\s*[xX]\s+(?<name>[A-Za-z][A-Za-z0-9'\u2019 -]{2,}(?:\s+\(Level\s+\d{1,2}\))?)(?=\s*$|[/\\\]\[""\|])",
        RegexOptions.Compiled);
    private static readonly Regex QuantityLineStart = new(
        @"^\s*\d+\s*[xX]\b",
        RegexOptions.Compiled);
    private static readonly Regex StandaloneQuantityLine = new(
        @"^\s*(?<qty>\d{1,3})\s*$",
        RegexOptions.Compiled);
    private static readonly Regex MissingLeadingDigitBeforeX = new(
        @"^\s*[xX]{1,2}\s+(?<name>[A-Za-z].{2,80}?)\s*$",
        RegexOptions.Compiled);
    private static readonly Regex OcrOneQuantityPrefix = new(
        @"^\s*[Il|]\s*[xX]\s+(?<name>[A-Za-z].{2,80}?)\s*$",
        RegexOptions.Compiled);
    private static readonly Regex RewardNameContinuation = new(
        @"^\s*(?<fragment>of|the|and)\s+(?<rest>[A-Za-z][A-Za-z0-9'\u2019 -]{1,})\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NoQuantityRewardCandidate = new(
        @"^\s*(?<name>[A-Za-z][A-Za-z0-9'\u2019 -]{5,}(?:\s+\(Level\s+\d{1,2}\))?)\s*$",
        RegexOptions.Compiled);
    private static readonly Regex QuantityLineWithTrailingFragment = new(
        @"^(?<prefix>\s*\d+\s*[xX]\s+.*?)(?<fragment>[A-Za-z]{1,5})$",
        RegexOptions.Compiled);
    private static readonly Regex LeadingWordFragment = new(
        @"^(?<fragment>[A-Za-z]{1,6})(?<rest>(?:'s|'S|er'?s|ER'?S)?(?:\s+.+)?)$",
        RegexOptions.Compiled);
    private static readonly JsonSerializerOptions VocabularyJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly Lazy<RuneshapingRewardVocabulary> RewardVocabulary = new(LoadRuneshapingRewardVocabulary);
    private static readonly IReadOnlyDictionary<string, string> RuneshapingPriceLookupAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ancient Rune of Horde"] = "Ancient Rune of the Horde",
            ["Rune of Blossom"] = "Rune of the Blossom",
            ["Rune of Prism"] = "Rune of the Prism",
            ["Saqawal's Rune of Sky"] = "Saqawals Rune Of The Sky"
        };

    private readonly string _debugDirectory;
    private PoeNinjaPrices? _cachedPrices;
    private DateTimeOffset _lastPriceRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset _lastMergedScanUtc = DateTimeOffset.MinValue;
    private readonly Dictionary<string, RawReward> _mergedRewards = new(StringComparer.OrdinalIgnoreCase);

    public RuneshapingScanner(string debugDirectory)
    {
        _debugDirectory = debugDirectory;
        Directory.CreateDirectory(_debugDirectory);
    }

    public async Task RefreshPricesAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        _cachedPrices = await PoeNinjaPrices.FetchAsync(cancellationToken, forceRefresh).ConfigureAwait(false);
        _lastPriceRefresh = DateTimeOffset.UtcNow;
    }

    public async Task<ScanResult> ScanScreenAsync(CancellationToken cancellationToken)
    {
        var screen = SelectTargetScreen();
        using var screenshot = CaptureScreen(screen.Bounds);
        return await ScanBitmapAsync(screenshot, screen.Bounds, mergeWithRecentRuneshapingScans: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ScanResult> ScanFileAsync(string screenshotPath, CancellationToken cancellationToken)
    {
        using var bitmap = new Bitmap(screenshotPath);
        return await ScanBitmapAsync(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height), mergeWithRecentRuneshapingScans: false, cancellationToken).ConfigureAwait(false);
    }

    public void ClearMergedRuneshapingRewards()
    {
        _mergedRewards.Clear();
        _lastMergedScanUtc = DateTimeOffset.MinValue;
    }

    internal static IReadOnlyList<string> RunParserSelfTest()
    {
        var vocabulary = RewardVocabulary.Value;
        var cases = new[]
        {
            new RuneshapingParserTestCase("missing leading digit", "x Runic Alloy", ["1x Runic Alloy"], []),
            new RuneshapingParserTestCase("split quantity one", "1\nx Swift Alloy", ["1x Swift Alloy"], []),
            new RuneshapingParserTestCase("split quantity one with Xx", "1\nXx Cyclonic Alloy", ["1x Cyclonic Alloy"], []),
            new RuneshapingParserTestCase("split quantity two with Xx", "2\nXx Artificer's Orb", ["2x Artificer's Orb"], []),
            new RuneshapingParserTestCase("split quantity four", "4\nx Blacksmith's Whetstone", ["4x Blacksmith's Whetstone"], []),
            new RuneshapingParserTestCase("split quantity six", "6\nx Armourer's Scrap", ["6x Armourer's Scrap"], []),
            new RuneshapingParserTestCase("joined rune continuation", "1x Ancient Rune\nof Animosity", ["1x Ancient Rune of Animosity"], ["1x Ancient Rune"]),
            new RuneshapingParserTestCase("no quantity canonical reward", "Uncut Spirit Gem", ["1x Uncut Spirit Gem"], []),
            new RuneshapingParserTestCase("tiny full vocabulary fallback", "2x Orb of Augmentatio", ["2x Orb of Augmentation"], []),
            new RuneshapingParserTestCase("one read as I", "Ix Warding Rune of Courage", ["1x Warding Rune of Courage"], []),
            new RuneshapingParserTestCase("one read as l with parenthesized level", "lx Thaumaturgic Flux (Level 19)", ["1x Thaumaturgic Flux (Level 19)"], []),
            new RuneshapingParserTestCase("one read as l with trailing slash", "lx Mystic Alloy/", ["1x Mystic Alloy"], []),
            new RuneshapingParserTestCase("one read as l with trailing bracket", "lx Orb of Alchemy]", ["1x Orb of Alchemy"], []),
            new RuneshapingParserTestCase("trailing quote on reward name", "5x Random Currency\"", ["5x Random Currency"], []),
            new RuneshapingParserTestCase("unique prefix completion", "2x Glassblower's", ["2x Glassblower's Bauble"], []),
            new RuneshapingParserTestCase("ambiguous prefix ignored", "1x Ancient Rune", [], ["1x Ancient Rune"]),
            new RuneshapingParserTestCase("short garbage suffix ignored", "1x Warding Rune of Ee", [], ["1x Warding Rune of Ee"])
        };

        var lines = new List<string>();
        var failures = new List<string>();
        foreach (var testCase in cases)
        {
            var rewards = ParseRewards(testCase.RawText, vocabulary, out _);
            var matches = MatchRewards(rewards, vocabulary);
            var accepted = matches
                .Where(match => match.Matched)
                .Select(match => $"{match.Reward.Quantity}x {match.Reward.ItemName}")
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var ignored = matches
                .Where(match => !match.Matched)
                .Select(match => $"{match.ParsedReward.Quantity}x {match.ParsedReward.ItemName}")
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var expectedAccepted = testCase.ExpectedAccepted
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var expectedIgnored = testCase.ExpectedIgnored
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var acceptedPass = accepted.SequenceEqual(expectedAccepted, StringComparer.OrdinalIgnoreCase);
            var ignoredPass = expectedIgnored.All(expected =>
                ignored.Contains(expected, StringComparer.OrdinalIgnoreCase));
            var pass = acceptedPass && ignoredPass;
            lines.Add($"{(pass ? "PASS" : "FAIL")} {testCase.Name}");
            lines.Add($"  accepted: {string.Join("; ", accepted.DefaultIfEmpty("(none)"))}");
            lines.Add($"  ignored: {string.Join("; ", ignored.DefaultIfEmpty("(none)"))}");

            if (!pass)
            {
                failures.Add(testCase.Name);
                lines.Add($"  expected accepted: {string.Join("; ", expectedAccepted.DefaultIfEmpty("(none)"))}");
                lines.Add($"  expected ignored: {string.Join("; ", expectedIgnored.DefaultIfEmpty("(none)"))}");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"Runeshaping parser self-test failed: {string.Join(", ", failures)}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");
        }

        return lines;
    }

    private async Task<ScanResult> ScanBitmapAsync(
        Bitmap screenshot,
        Rectangle screenBounds,
        bool mergeWithRecentRuneshapingScans,
        CancellationToken cancellationToken)
    {
        var debugImageDirectory = CreateRuneshapingDebugImageDirectory();
        SaveBitmap(screenshot, Path.Combine(debugImageDirectory, "01-raw-capture.png"));

        var cropRegion = ResolveCropRegion(screenshot.Size);
        using var crop = screenshot.Clone(cropRegion, screenshot.PixelFormat);
        var panelLooksScrollable = LooksLikeScrollableRewardPanel(crop);
        SaveBitmap(crop, Path.Combine(_debugDirectory, "runeshaping-crop.png"));
        SaveBitmap(crop, Path.Combine(debugImageDirectory, "02-runeshaping-panel-crop.png"));

        using var processed = PrepareForOcr(crop);
        var processedPath = Path.Combine(_debugDirectory, "runeshaping-ocr.png");
        SaveBitmap(processed, processedPath);
        SaveBitmap(processed, Path.Combine(debugImageDirectory, "03-preprocessed-ocr-input.png"));

        var tessData = await EnsureTessDataAsync(Path.Combine(AppContext.BaseDirectory, "tessdata"), cancellationToken).ConfigureAwait(false);
        var ocrResult = await RuneshapingOcrPipeline.RecognizeAsync(processedPath, tessData, cancellationToken).ConfigureAwait(false);
        var rawText = ocrResult.Text;
        File.WriteAllText(Path.Combine(_debugDirectory, "runeshaping-ocr.txt"), rawText);
        var vocabulary = RewardVocabulary.Value;
        var rewards = ParseRewards(rawText, vocabulary, out var rewardParseCandidates);

        if (_cachedPrices is null || DateTimeOffset.UtcNow - _lastPriceRefresh > TimeSpan.FromMinutes(30))
        {
            await RefreshPricesAsync(cancellationToken).ConfigureAwait(false);
        }

        var prices = _cachedPrices ?? throw new InvalidOperationException("Runeshaping price cache was not initialized.");
        var notes = new List<string>();
        var matchedRewards = MatchRewards(rewards, vocabulary);
        var acceptedMatches = matchedRewards
            .Where(match => match.Matched)
            .ToArray();
        var ignoredRewards = matchedRewards
            .Where(match => !match.Matched)
            .ToArray();
        var rowRetryAttempts = await RetryIgnoredRuneshapingRowsAsync(
            ocrResult,
            crop,
            debugImageDirectory,
            tessData,
            vocabulary,
            cancellationToken).ConfigureAwait(false);
        var retryRecoveredRewards = rowRetryAttempts
            .Where(attempt => attempt.Accepted && attempt.Reward is not null)
            .Select(attempt => attempt.Reward!)
            .ToArray();
        var canonicalRewards = acceptedMatches
            .Select(match => match.Reward)
            .Concat(retryRecoveredRewards)
            .ToArray();
        var rewardsForPricing = mergeWithRecentRuneshapingScans
            ? MergeRewardsForCurrentEncounter(canonicalRewards, notes)
            : canonicalRewards;

        if (panelLooksScrollable || canonicalRewards.Length >= 8)
        {
            notes.Add("Reward list may be scrollable. Scroll the panel and press F8 again to merge hidden rows.");
        }

        var currentVisibleColored = AssignColors(BuildPricedChoices(canonicalRewards, prices, out _));
        var priced = BuildPricedChoices(rewardsForPricing, prices, out var unpriced);

        var colored = AssignColors(priced);
        var overlayLabels = BuildOverlayLabels(ocrResult, rowRetryAttempts, currentVisibleColored, prices, vocabulary, cropRegion, screenBounds);
        var overlayDebugLines = overlayLabels.Count == 0
            ? [
                currentVisibleColored.Count == 0
                    ? "(none - no current visible priced rewards)"
                    : "(none - current visible priced rewards could not be placed)"
              ]
            : overlayLabels.Select(FormatOverlayLabelDebugLine);
        File.WriteAllLines(
            Path.Combine(_debugDirectory, "runeshaping-debug.txt"),
            new[]
            {
                $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} local",
                $"App version: {Application.ProductVersion}",
                $"Debug images: {debugImageDirectory}",
                $"OCR backend: {ocrResult.Backend}",
                string.IsNullOrWhiteSpace(ocrResult.FallbackReason)
                    ? "OCR fallback reason: (none)"
                    : $"OCR fallback reason: {ocrResult.FallbackReason}",
                $"Screenshot size: {screenshot.Width}x{screenshot.Height}",
                $"Screen bounds: {FormatRectangle(screenBounds)}",
                $"Crop region: {FormatRectangle(cropRegion)}",
                $"Processed OCR scale: {RuneshapingOcrScale:0.#}x",
                $"Overlay window bounds: {FormatRectangle(screenBounds)}",
                string.Empty,
                "Raw OCR:",
                rawText,
                string.Empty,
                "Structured OCR lines:",
            }
                .Concat(FormatStructuredOcrDebugLines(ocrResult))
                .Concat(new[]
                {
                string.Empty,
                "Parsed rewards:"
                })
                .Concat(rewards.Select(reward => $"{reward.Quantity}x {reward.ItemName}"))
                .Concat(new[] { string.Empty, "Reward parse candidates:" })
                .Concat(rewardParseCandidates.Count == 0
                    ? ["(none)"]
                    : rewardParseCandidates.Select(FormatParseCandidateDebugLine))
                .Concat(new[] { string.Empty, "Canonical match results:" })
                .Concat(matchedRewards.Select(match => FormatMatchDebugLine(match, prices)))
                .Concat(new[] { string.Empty, "Ignored low-confidence OCR rewards:" })
                .Concat(ignoredRewards.Length == 0
                    ? ["(none)"]
                    : ignoredRewards.Select(match => FormatIgnoredRewardDebugLine(match, vocabulary)))
                .Concat(new[] { string.Empty, "Row-level OCR retry:" })
                .Concat(FormatRowRetryDebugLines(rowRetryAttempts))
                .Concat(new[] { string.Empty, "Runeshaping vocabulary:" })
                .Concat([$"{vocabulary.CanonicalNames.Count} canonical rewards loaded from {vocabulary.SourcePath}"])
                .Concat(string.IsNullOrWhiteSpace(vocabulary.LoadError) ? [] : [$"load warning: {vocabulary.LoadError}"])
                .Concat(new[] { string.Empty, "Merged rewards:" })
                .Concat(rewardsForPricing.Select(reward => $"{reward.Quantity}x {reward.ItemName}"))
                .Concat(new[] { string.Empty, "Notes:" })
                .Concat(notes.DefaultIfEmpty("(none)"))
                .Concat(new[] { string.Empty, "Unpriced:" })
                .Concat(unpriced.DefaultIfEmpty("(none)"))
                .Concat(new[] { string.Empty, "Overlay labels:" })
                .Concat(overlayDebugLines)
                .Concat(new[] { string.Empty, "Unpriced diagnostics:" })
                .Concat(unpriced.Count == 0
                    ? ["(none)"]
                    : unpriced.Select(rewardText =>
                    {
                        var match = Regex.Match(rewardText, @"^\d+x\s+(?<name>.+)$");
                        var name = match.Success ? match.Groups["name"].Value : rewardText;
                        return prices.DiagnoseMissing(ResolveRuneshapingPriceLookupName(name)).ToDebugString();
                    })));
        return new ScanResult(colored, unpriced, notes, rawText, cropRegion, screenBounds, overlayLabels);
    }

    private static List<RewardChoice> BuildPricedChoices(
        IReadOnlyList<RawReward> rewards,
        PoeNinjaPrices prices,
        out List<string> unpriced)
    {
        var priced = new List<RewardChoice>();
        unpriced = [];
        foreach (var reward in rewards)
        {
            var priceLookupName = ResolveRuneshapingPriceLookupName(reward.ItemName);
            var value = prices.TryGetValue(priceLookupName, reward.Quantity);
            if (value is null)
            {
                unpriced.Add($"{reward.Quantity}x {reward.ItemName}");
                continue;
            }

            priced.Add(new RewardChoice(
                reward.Quantity,
                reward.ItemName,
                value.Exalts,
                value.Divines,
                ChoiceColor.Red));
        }

        return priced;
    }

    private static async Task<IReadOnlyList<RowOcrRetryAttempt>> RetryIgnoredRuneshapingRowsAsync(
        RuneshapingOcrResult ocrResult,
        Bitmap panelCrop,
        string debugImageDirectory,
        string tessData,
        RuneshapingRewardVocabulary vocabulary,
        CancellationToken cancellationToken)
    {
        if (ocrResult.Lines.Count == 0)
        {
            return [];
        }

        var attempts = new List<RowOcrRetryAttempt>();
        var imageIndex = 4;
        foreach (var line in ocrResult.Lines.Where(line => line.BoundsRectangle is not null))
        {
            var lineRewards = ParseRewards(line.Text, vocabulary, out _);
            var lineMatches = MatchRewards(lineRewards, vocabulary).ToArray();
            if (lineMatches.Any(match => match.Matched) ||
                !ShouldRetryOcrLine(line.Text, lineRewards, lineMatches))
            {
                continue;
            }

            var retryCropBounds = ResolveRowRetryCropBounds(line.BoundsRectangle!.Value, panelCrop.Size);
            try
            {
                using var retrySource = panelCrop.Clone(retryCropBounds, panelCrop.PixelFormat);
                var sourceImagePath = Path.Combine(
                    debugImageDirectory,
                    $"{imageIndex++:00}-row-retry-line-{line.LineNumber:00}-source.png");
                SaveBitmap(retrySource, sourceImagePath);

                using var retryProcessed = PrepareRowRetryForOcr(retrySource);
                var processedImagePath = Path.Combine(
                    debugImageDirectory,
                    $"{imageIndex++:00}-row-retry-line-{line.LineNumber:00}-preprocessed.png");
                SaveBitmap(retryProcessed, processedImagePath);

                var retryOcrResult = await RuneshapingOcrPipeline
                    .RecognizeAsync(processedImagePath, tessData, cancellationToken)
                    .ConfigureAwait(false);
                var retryRawText = retryOcrResult.Text;
                var retryMatch = MatchSingleRetriedReward(retryRawText, vocabulary, out var rejectionReason);
                attempts.Add(new RowOcrRetryAttempt(
                    line.LineNumber,
                    line.Text,
                    line.Bounds,
                    retryCropBounds,
                    RowRetryPreprocessingMode,
                    retryOcrResult.Backend,
                    retryRawText,
                    retryMatch is not null,
                    retryMatch?.Reward,
                    retryMatch?.Method ?? string.Empty,
                    rejectionReason));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                attempts.Add(new RowOcrRetryAttempt(
                    line.LineNumber,
                    line.Text,
                    line.Bounds,
                    retryCropBounds,
                    RowRetryPreprocessingMode,
                    "failed",
                    string.Empty,
                    false,
                    null,
                    string.Empty,
                    $"{ex.GetType().Name}: {ex.Message}"));
            }
        }

        return attempts;
    }

    private static bool ShouldRetryOcrLine(
        string text,
        IReadOnlyList<RawReward> lineRewards,
        IReadOnlyList<RuneshapingRewardMatch> lineMatches)
    {
        if (lineRewards.Count > 0 && lineMatches.All(match => !match.Matched))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return Regex.IsMatch(text, @"\b(?:\d+|[Il|])\s*[xX]\b|^\s*[xX]{1,2}\s+", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(text, @"\b(Rune|Orb|Alloy|Gem|Currency|Scrap|Whetstone|Bauble)\b", RegexOptions.IgnoreCase);
    }

    private static Rectangle ResolveRowRetryCropBounds(RectangleF processedBounds, Size panelCropSize)
    {
        var rowLeft = processedBounds.Left / RuneshapingOcrScale;
        var rowTop = processedBounds.Top / RuneshapingOcrScale;
        var rowRight = processedBounds.Right / RuneshapingOcrScale;
        var rowBottom = processedBounds.Bottom / RuneshapingOcrScale;
        var leftPadding = ScaleCropWidth(panelCropSize.Width, 32);
        var textAreaLeft = ScaleCropWidth(panelCropSize.Width, 110);
        var rightPadding = ScaleCropWidth(panelCropSize.Width, 24);
        var verticalPadding = Math.Max(8, (int)Math.Round((rowBottom - rowTop) * 0.9));

        var left = Math.Max(0, Math.Min((int)Math.Floor(rowLeft) - leftPadding, textAreaLeft));
        var top = Math.Max(0, (int)Math.Floor(rowTop) - verticalPadding);
        var right = Math.Min(panelCropSize.Width, Math.Max((int)Math.Ceiling(rowRight) + leftPadding, panelCropSize.Width - rightPadding));
        var bottom = Math.Min(panelCropSize.Height, (int)Math.Ceiling(rowBottom) + verticalPadding);

        if (right <= left)
        {
            right = Math.Min(panelCropSize.Width, left + Math.Max(1, panelCropSize.Width - left));
        }

        if (bottom <= top)
        {
            bottom = Math.Min(panelCropSize.Height, top + Math.Max(1, ScaleCropHeight(panelCropSize.Height, 36)));
        }

        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static Bitmap PrepareRowRetryForOcr(Bitmap input)
    {
        const int scale = 4;
        const int border = 18;
        var output = new Bitmap(input.Width * scale + border * 2, input.Height * scale + border * 2, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(input, border, border, input.Width * scale, input.Height * scale);
        }

        for (var y = 0; y < output.Height; y++)
        {
            for (var x = 0; x < output.Width; x++)
            {
                var color = output.GetPixel(x, y);
                var luminance = (int)(0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
                var neutralInk = Math.Abs(color.R - color.G) < 65 && Math.Abs(color.G - color.B) < 65;
                var likelyText = luminance < 150 && neutralInk;
                output.SetPixel(x, y, likelyText ? Color.Black : Color.White);
            }
        }

        return output;
    }

    private static RuneshapingRewardMatch? MatchSingleRetriedReward(
        string rawText,
        RuneshapingRewardVocabulary vocabulary,
        out string rejectionReason)
    {
        var rewards = ParseRewards(rawText, vocabulary, out _);
        if (rewards.Count == 0)
        {
            rejectionReason = "noParsedReward";
            return null;
        }

        var matches = MatchRewards(rewards, vocabulary)
            .Where(match => match.Matched)
            .ToArray();
        if (matches.Length == 1)
        {
            rejectionReason = string.Empty;
            return matches[0];
        }

        rejectionReason = matches.Length == 0
            ? "ambiguousOrNoCanonicalMatch"
            : $"multipleCanonicalMatches:{matches.Length}";
        return null;
    }

    private static IReadOnlyList<string> FormatRowRetryDebugLines(IReadOnlyList<RowOcrRetryAttempt> attempts)
    {
        if (attempts.Count == 0)
        {
            return ["(none)"];
        }

        var lines = new List<string>();
        foreach (var attempt in attempts)
        {
            var bounds = string.IsNullOrWhiteSpace(attempt.SourceBounds)
                ? "(none)"
                : $"({attempt.SourceBounds})";
            lines.Add($"line-{attempt.LineNumber:00} source='{NormalizeDebugText(attempt.SourceText)}' bounds={bounds}");
            lines.Add($"  retry crop=({FormatRectangle(attempt.RetryCropBounds)})");
            lines.Add($"  mode={attempt.Mode} backend={attempt.Backend}");
            lines.Add($"  raw='{NormalizeDebugText(attempt.RawText)}'");
            lines.Add(attempt.Accepted && attempt.Reward is not null
                ? $"  accepted canonical='{attempt.Reward.ItemName}' method={attempt.Method}"
                : $"  rejected reason={attempt.RejectionReason}");
        }

        return lines;
    }

    private static string NormalizeDebugText(string value)
    {
        var text = Regex.Replace(value, @"\s+", " ").Trim();
        return text.Length <= 140 ? text : text[..137] + "...";
    }

    private IReadOnlyList<RawReward> MergeRewardsForCurrentEncounter(IReadOnlyList<RawReward> visibleRewards, List<string> notes)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastMergedScanUtc > TimeSpan.FromMinutes(2))
        {
            _mergedRewards.Clear();
        }

        _lastMergedScanUtc = now;

        foreach (var reward in visibleRewards)
        {
            _mergedRewards.TryAdd($"{reward.Quantity}x {reward.ItemName}", reward);
        }

        if (_mergedRewards.Count > visibleRewards.Count)
        {
            notes.Add($"Merged {_mergedRewards.Count} rewards from this Runeshaping panel. It resets after two minutes without scanning.");
        }

        return _mergedRewards.Values
            .OrderByDescending(reward => reward.Quantity)
            .ThenBy(reward => reward.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<RewardChoice> AssignColors(List<RewardChoice> choices)
    {
        if (choices.Count == 0)
        {
            return choices;
        }

        var sorted = choices
            .OrderByDescending(choice => choice.Exalts)
            .ThenBy(choice => choice.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var best = sorted[0].Exalts;
        if (best <= 0)
        {
            return sorted;
        }

        return sorted
            .Select(choice =>
            {
                var color =
                    choice.Exalts >= best * 0.99m ? ChoiceColor.Green :
                    choice.Exalts >= best * 0.25m ? ChoiceColor.Yellow :
                    ChoiceColor.Red;

                return choice with { Color = color };
            })
            .ToArray();
    }

    private static IReadOnlyList<RuneshapingOverlayLabel> BuildOverlayLabels(
        RuneshapingOcrResult ocrResult,
        IReadOnlyList<RowOcrRetryAttempt> rowRetryAttempts,
        IReadOnlyList<RewardChoice> coloredChoices,
        PoeNinjaPrices prices,
        RuneshapingRewardVocabulary vocabulary,
        Rectangle cropRegion,
        Rectangle screenBounds)
    {
        if (coloredChoices.Count == 0)
        {
            return [];
        }

        var coloredByReward = coloredChoices
            .GroupBy(choice => RewardKey(choice.Quantity, choice.ItemName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var labels = new List<RuneshapingOverlayLabel>();
        var retryByLineNumber = rowRetryAttempts
            .Where(attempt => attempt.Accepted && attempt.Reward is not null)
            .GroupBy(attempt => attempt.LineNumber)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var line in ocrResult.Lines.Where(line => line.BoundsRectangle is not null))
        {
            var lineRewards = ParseRewards(line.Text, vocabulary, out _);
            var lineMatches = MatchRewards(lineRewards, vocabulary)
                .Where(match => match.Matched)
                .ToArray();
            if (lineMatches.Length == 0 &&
                retryByLineNumber.TryGetValue(line.LineNumber, out var retryAttempt) &&
                retryAttempt.Reward is not null)
            {
                lineMatches =
                [
                    RuneshapingRewardMatch.Exact(
                        retryAttempt.Reward,
                        retryAttempt.Reward.ItemName,
                        PoeNinjaPrices.Normalize(retryAttempt.Reward.ItemName))
                ];
            }

            var lineSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var match in lineMatches)
            {
                var priceLookupName = ResolveRuneshapingPriceLookupName(match.Reward.ItemName);
                var value = prices.TryGetValue(priceLookupName, match.Reward.Quantity);
                if (value is null)
                {
                    continue;
                }

                var key = RewardKey(match.Reward.Quantity, match.Reward.ItemName);
                if (!lineSeen.Add(key))
                {
                    continue;
                }

                var choice = coloredByReward.TryGetValue(key, out var colored)
                    ? colored
                    : new RewardChoice(match.Reward.Quantity, match.Reward.ItemName, value.Exalts, value.Divines, ChoiceColor.Red);
                var rowBounds = MapProcessedOcrBoundsToScreen(line.BoundsRectangle!.Value, cropRegion, screenBounds);
                var placement = ResolveInlineLabelPlacement(
                    choice.CompactPriceText,
                    rowBounds,
                    labels.Count,
                    coloredChoices.Count,
                    cropRegion,
                    screenBounds);
                labels.Add(new RuneshapingOverlayLabel(
                    choice.CompactPriceText,
                    choice.Color,
                    placement.Bounds,
                    rowBounds,
                    $"ocr-row-{placement.Mode}",
                    choice.CompactPriceBasis));
            }
        }

        if (labels.Count > 0)
        {
            return labels;
        }

        return coloredChoices
            .Take(12)
            .Select((choice, index) =>
            {
                var placement = ResolveInlineLabelPlacement(
                    choice.CompactPriceText,
                    null,
                    index,
                    Math.Min(12, coloredChoices.Count),
                    cropRegion,
                    screenBounds);
                return new RuneshapingOverlayLabel(
                    choice.CompactPriceText,
                    choice.Color,
                    placement.Bounds,
                    null,
                    $"panel-spacing-{placement.Mode}",
                    choice.CompactPriceBasis);
            })
            .ToArray();
    }

    private static Rectangle MapProcessedOcrBoundsToScreen(
        RectangleF processedBounds,
        Rectangle cropRegion,
        Rectangle screenBounds)
    {
        return Rectangle.Round(new RectangleF(
            screenBounds.Left + cropRegion.Left + processedBounds.X / RuneshapingOcrScale,
            screenBounds.Top + cropRegion.Top + processedBounds.Y / RuneshapingOcrScale,
            Math.Max(1f, processedBounds.Width / RuneshapingOcrScale),
            Math.Max(1f, processedBounds.Height / RuneshapingOcrScale)));
    }

    private static string FormatOverlayLabelDebugLine(RuneshapingOverlayLabel label)
    {
        var rowBounds = label.RowBounds is null
            ? "fallback-spacing"
            : $"rowBounds={label.RowBounds.Value.X},{label.RowBounds.Value.Y},{label.RowBounds.Value.Width},{label.RowBounds.Value.Height}";
        return $"{label.Text} color={label.Color} mode={label.PlacementMode} valueBasis='{label.ValueBasis}' {rowBounds} labelBounds={FormatRectangle(label.LabelBounds)}";
    }

    private static string RewardKey(int quantity, string itemName)
    {
        return $"{quantity}x {itemName}";
    }

    private static InlineLabelPlacement ResolveInlineLabelPlacement(
        string text,
        Rectangle? rowBounds,
        int index,
        int labelCount,
        Rectangle cropRegion,
        Rectangle screenBounds)
    {
        var panelBounds = new Rectangle(
            screenBounds.Left + cropRegion.Left,
            screenBounds.Top + cropRegion.Top,
            cropRegion.Width,
            cropRegion.Height);
        var labelSize = MeasureInlineLabelSize(text);
        var outsidePadding = Math.Max(6, ScalePanelLength(panelBounds.Width, 10));
        var insidePadding = Math.Max(6, ScalePanelLength(panelBounds.Width, 10));
        var outsideX = panelBounds.Right + outsidePadding;
        var useOutside = outsideX + labelSize.Width <= screenBounds.Right - 8;
        var x = useOutside
            ? outsideX
            : panelBounds.Right - labelSize.Width - insidePadding;
        var y = rowBounds is null
            ? ResolveFallbackInlineY(panelBounds, index, labelCount, labelSize.Height)
            : rowBounds.Value.Top + (rowBounds.Value.Height - labelSize.Height) / 2;

        if (rowBounds is not null && x < panelBounds.Right && rowBounds.Value.Right + 10 > x)
        {
            x = Math.Min(rowBounds.Value.Right + 10, panelBounds.Right - labelSize.Width - 6);
        }

        x = Math.Clamp(x, screenBounds.Left + 8, screenBounds.Right - labelSize.Width - 8);
        y = Math.Clamp(y, screenBounds.Top + 8, screenBounds.Bottom - labelSize.Height - 8);
        return new InlineLabelPlacement(
            new Rectangle(x, y, labelSize.Width, labelSize.Height),
            useOutside ? "outside-right" : "inside-right");
    }

    private static Size MeasureInlineLabelSize(string text)
    {
        var roughWidth = text.Length * 8 + 22;
        return new Size(Math.Clamp(roughWidth, InlineOverlayMinLabelWidth, InlineOverlayMaxLabelWidth), InlineOverlayLabelHeight);
    }

    private static int ResolveFallbackInlineY(Rectangle panelBounds, int index, int labelCount, int labelHeight)
    {
        var top = panelBounds.Top + ScalePanelLength(panelBounds.Height, 150);
        var step = ScalePanelLength(panelBounds.Height, 54);
        var y = top + index * step;
        var maxY = panelBounds.Bottom - labelHeight - 24;
        if (labelCount > 1 && y > maxY)
        {
            step = Math.Max(labelHeight + 4, (maxY - top) / Math.Max(1, labelCount - 1));
            y = top + index * step;
        }

        return y;
    }

    private static int ScalePanelLength(int actualLength, int base3840Length)
    {
        return Math.Max(1, (int)Math.Round(base3840Length * actualLength / 1080d));
    }

    private static int ScaleCropWidth(int actualWidth, int base3840Width)
    {
        return Math.Max(1, (int)Math.Round(base3840Width * actualWidth / (double)Crop3840x2160.Width));
    }

    private static int ScaleCropHeight(int actualHeight, int base3840Height)
    {
        return Math.Max(1, (int)Math.Round(base3840Height * actualHeight / (double)Crop3840x2160.Height));
    }

    private static string FormatRectangle(Rectangle rectangle)
    {
        return $"{rectangle.X},{rectangle.Y},{rectangle.Width},{rectangle.Height}";
    }

    private sealed record InlineLabelPlacement(Rectangle Bounds, string Mode);

    private static IReadOnlyList<RawReward> ParseRewards(
        string rawText,
        RuneshapingRewardVocabulary vocabulary,
        out IReadOnlyList<RewardParseCandidate> parseCandidates)
    {
        parseCandidates = BuildRewardParseCandidates(rawText, vocabulary);
        var rewards = new List<RawReward>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var gluedParseCandidateKeys = parseCandidates
            .Where(candidate => candidate.Source.Equals("stitched-glued", StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.StitchKey)
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in parseCandidates)
        {
            if (candidate.Source.Equals("stitched-spaced", StringComparison.OrdinalIgnoreCase) &&
                gluedParseCandidateKeys.Contains(candidate.StitchKey))
            {
                continue;
            }

            foreach (Match match in RewardLine.Matches(candidate.Text))
            {
                if (!int.TryParse(match.Groups["qty"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) ||
                    quantity <= 0)
                {
                    continue;
                }

                var itemName = NormalizeParsedRewardName(match.Groups["name"].Value);
                if (itemName.Length == 0 || itemName.Length > 80)
                {
                    continue;
                }

                var key = $"{quantity}x {itemName}";
                if (seen.Add(key))
                {
                    rewards.Add(new RawReward(quantity, itemName));
                }
            }
        }

        return rewards;
    }

    private static IReadOnlyList<RewardParseCandidate> BuildRewardParseCandidates(
        string rawText,
        RuneshapingRewardVocabulary vocabulary)
    {
        var sourceLines = rawText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        var candidates = new List<RewardParseCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < sourceLines.Length; index++)
        {
            var line = sourceLines[index];
            AddParseCandidate("original", line, candidates, seen);
            AddRepairedParseCandidates(sourceLines, index, vocabulary, candidates, seen);

            if (!QuantityLineStart.IsMatch(line) || RewardLine.IsMatch(line))
            {
                continue;
            }

            var combined = line;
            var joinedLines = 0;
            for (var nextIndex = index + 1; nextIndex < sourceLines.Length && joinedLines < 2; nextIndex++)
            {
                var next = sourceLines[nextIndex];
                if (QuantityLineStart.IsMatch(next) || next.Length > 40)
                {
                    break;
                }

                var candidate = combined + " " + next;
                if (candidate.Length > 90)
                {
                    break;
                }

                combined = candidate;
                joinedLines++;
                var stitchKey = $"{index}:{nextIndex}";
                AddParseCandidate("stitched-spaced", combined, candidates, seen, stitchKey);
                if (TryBuildGluedWordCandidate(line, next, out var gluedCandidate))
                {
                    AddParseCandidate("stitched-glued", gluedCandidate, candidates, seen, stitchKey);
                }

                if (RewardLine.IsMatch(combined))
                {
                    break;
                }
            }
        }

        return candidates;
    }

    private static void AddRepairedParseCandidates(
        IReadOnlyList<string> sourceLines,
        int index,
        RuneshapingRewardVocabulary vocabulary,
        List<RewardParseCandidate> candidates,
        HashSet<string> seen)
    {
        var line = sourceLines[index];

        var ocrOneMatch = OcrOneQuantityPrefix.Match(line);
        if (ocrOneMatch.Success)
        {
            var itemName = NormalizeParsedRewardName(ocrOneMatch.Groups["name"].Value, out var trimmedTrailingJunk);
            AddSafeRepairedCandidate(
                "ocr-repair",
                $"1x {itemName}",
                AppendTrailingOcrJunkRepairReason(
                    $"quantityNormalized=1 reason=ocrOneReadAsOne sourceLine={index}",
                    trimmedTrailingJunk),
                [line],
                vocabulary,
                candidates,
                seen);
        }

        var previousLineIsStandaloneQuantity = index > 0 && StandaloneQuantityLine.IsMatch(sourceLines[index - 1]);
        var missingDigitMatch = MissingLeadingDigitBeforeX.Match(line);
        if (missingDigitMatch.Success && !previousLineIsStandaloneQuantity)
        {
            var itemName = NormalizeParsedRewardName(missingDigitMatch.Groups["name"].Value, out var trimmedTrailingJunk);
            AddSafeRepairedCandidate(
                "ocr-repair",
                $"1x {itemName}",
                AppendTrailingOcrJunkRepairReason(
                    $"quantityInferred=1 reason=missingLeadingDigitBeforeX sourceLine={index}",
                    trimmedTrailingJunk),
                [line],
                vocabulary,
                candidates,
                seen);
        }

        var standaloneQuantityMatch = StandaloneQuantityLine.Match(line);
        if (standaloneQuantityMatch.Success)
        {
            var quantityText = standaloneQuantityMatch.Groups["qty"].Value;
            for (var nextIndex = index + 1; nextIndex < sourceLines.Count && nextIndex <= index + 2; nextIndex++)
            {
                var nextLine = sourceLines[nextIndex];
                var nextMatch = MissingLeadingDigitBeforeX.Match(nextLine);
                if (!nextMatch.Success)
                {
                    if (QuantityLineStart.IsMatch(nextLine) || StandaloneQuantityLine.IsMatch(nextLine))
                    {
                        break;
                    }

                    continue;
                }

                var itemName = NormalizeParsedRewardName(nextMatch.Groups["name"].Value, out var trimmedTrailingJunk);
                AddSafeRepairedCandidate(
                    "ocr-repair",
                    $"{quantityText}x {itemName}",
                    AppendTrailingOcrJunkRepairReason(
                        $"quantityReconstructed={quantityText} reason=splitQuantityBeforeX sourceLines={index},{nextIndex}",
                        trimmedTrailingJunk),
                    [line, nextLine],
                    vocabulary,
                    candidates,
                    seen);
                break;
            }
        }

        var rewardMatch = RewardLine.Match(line);
        if (rewardMatch.Success && index + 1 < sourceLines.Count)
        {
            var continuation = RewardNameContinuation.Match(sourceLines[index + 1]);
            if (continuation.Success)
            {
                var itemName = NormalizeParsedRewardName(
                    $"{rewardMatch.Groups["name"].Value} {continuation.Groups["fragment"].Value} {continuation.Groups["rest"].Value}",
                    out var trimmedTrailingJunk);
                AddSafeRepairedCandidate(
                    "ocr-repair",
                    $"{rewardMatch.Groups["qty"].Value}x {itemName}",
                    AppendTrailingOcrJunkRepairReason(
                        $"reason=joinedRewardNameContinuation sourceLines={index},{index + 1}",
                        trimmedTrailingJunk),
                    [line, sourceLines[index + 1]],
                    vocabulary,
                    candidates,
                    seen);
            }
        }

        if (!previousLineIsStandaloneQuantity &&
            !QuantityLineStart.IsMatch(line) &&
            !StandaloneQuantityLine.IsMatch(line) &&
            !MissingLeadingDigitBeforeX.IsMatch(line) &&
            !OcrOneQuantityPrefix.IsMatch(line))
        {
            var noQuantityMatch = NoQuantityRewardCandidate.Match(line);
            if (noQuantityMatch.Success)
            {
                var itemName = NormalizeParsedRewardName(noQuantityMatch.Groups["name"].Value, out var trimmedTrailingJunk);
                AddSafeRepairedCandidate(
                    "ocr-repair",
                    $"1x {itemName}",
                    AppendTrailingOcrJunkRepairReason(
                        $"quantityInferred=1 reason=noQuantityCanonicalReward sourceLine={index}",
                        trimmedTrailingJunk),
                    [line],
                    vocabulary,
                    candidates,
                    seen);
            }
        }
    }

    private static void AddSafeRepairedCandidate(
        string source,
        string candidate,
        string repairReason,
        IReadOnlyList<string> sourceLines,
        RuneshapingRewardVocabulary vocabulary,
        List<RewardParseCandidate> candidates,
        HashSet<string> seen)
    {
        var rewardMatch = RewardLine.Match(candidate);
        if (!rewardMatch.Success)
        {
            return;
        }

        var itemName = NormalizeParsedRewardName(rewardMatch.Groups["name"].Value);
        if (!IsSafeVocabularyRewardCandidate(itemName, vocabulary))
        {
            return;
        }

        AddParseCandidate(source, candidate, candidates, seen, repairReason: repairReason, sourceLines: sourceLines);
    }

    private static bool IsSafeVocabularyRewardCandidate(
        string itemName,
        RuneshapingRewardVocabulary vocabulary)
    {
        return MatchRewardName(new RawReward(1, itemName), vocabulary).Matched;
    }

    private static bool TryBuildGluedWordCandidate(string firstLine, string nextLine, out string candidate)
    {
        candidate = string.Empty;

        var firstMatch = QuantityLineWithTrailingFragment.Match(firstLine);
        if (!firstMatch.Success)
        {
            return false;
        }

        var nextMatch = LeadingWordFragment.Match(nextLine);
        if (!nextMatch.Success)
        {
            return false;
        }

        var prefix = firstMatch.Groups["prefix"].Value;
        var firstFragment = firstMatch.Groups["fragment"].Value;
        var nextFragment = nextMatch.Groups["fragment"].Value;
        var nextRest = nextMatch.Groups["rest"].Value;
        candidate = prefix + firstFragment + nextFragment + nextRest;
        return candidate.Length <= 90;
    }

    private static void AddParseCandidate(
        string source,
        string candidate,
        List<RewardParseCandidate> candidates,
        HashSet<string> seen,
        string stitchKey = "",
        string repairReason = "",
        IReadOnlyList<string>? sourceLines = null)
    {
        if (seen.Add(candidate))
        {
            candidates.Add(new RewardParseCandidate(source, candidate, stitchKey, repairReason, sourceLines ?? []));
        }
    }

    private static string FormatParseCandidateDebugLine(RewardParseCandidate candidate)
    {
        var repairReason = string.IsNullOrWhiteSpace(candidate.RepairReason)
            ? string.Empty
            : $" {candidate.RepairReason}";
        var sourceLineValues = candidate.SourceLines ?? [];
        var sourceLines = sourceLineValues.Count == 0
            ? string.Empty
            : $" sourceText='{string.Join(" | ", sourceLineValues)}'";
        return $"{candidate.Source}: {candidate.Text}{repairReason}{sourceLines}";
    }

    private static IReadOnlyList<string> FormatStructuredOcrDebugLines(RuneshapingOcrResult result)
    {
        if (!result.Backend.Equals("windows", StringComparison.OrdinalIgnoreCase))
        {
            return [$"(not available for {result.Backend} backend)"];
        }

        if (result.Lines.Count == 0)
        {
            return ["(none)"];
        }

        var lines = new List<string>();
        foreach (var line in result.Lines)
        {
            var bounds = string.IsNullOrWhiteSpace(line.Bounds)
                ? string.Empty
                : $" bounds=({line.Bounds})";
            lines.Add($"line-{line.LineNumber:00} text='{line.Text}'{bounds}");

            foreach (var word in line.Words)
            {
                var wordBounds = string.IsNullOrWhiteSpace(word.Bounds)
                    ? string.Empty
                    : $" bounds=({word.Bounds})";
                lines.Add($"  word-{word.WordNumber:00} text='{word.Text}'{wordBounds}");
            }
        }

        return lines;
    }

    private static string NormalizeParsedRewardName(string value)
    {
        return NormalizeParsedRewardName(value, out _);
    }

    private static string NormalizeParsedRewardName(string value, out bool trimmedTrailingJunk)
    {
        var original = value.Trim();
        var trimmed = TrimTrailingOcrJunk(original);
        trimmedTrailingJunk = !trimmed.Equals(original, StringComparison.Ordinal);
        return NormalizeItemName(trimmed);
    }

    private static string TrimTrailingOcrJunk(string value)
    {
        return Regex.Replace(value, @"[/\\\]\[""\|]+$", string.Empty).Trim();
    }

    private static string AppendTrailingOcrJunkRepairReason(string repairReason, bool trimmedTrailingJunk)
    {
        return trimmedTrailingJunk
            ? $"{repairReason} trailingOcrJunkTrimmed=true"
            : repairReason;
    }

    private static string NormalizeItemName(string value)
    {
        var asciiValue = value.Replace("\u2019", "'");
        var cleaned = Regex.Replace(asciiValue, @"\s+", " ").Trim(' ', '-', '\'');
        var titled = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cleaned.ToLowerInvariant());
        titled = Regex.Replace(titled, @"'S\b", "'s", RegexOptions.IgnoreCase);
        return Regex.Replace(titled, @"\b(Of|The|And)\b", m => m.Value.ToLowerInvariant());
    }

    private static IReadOnlyList<RuneshapingRewardMatch> MatchRewards(
        IReadOnlyList<RawReward> rewards,
        RuneshapingRewardVocabulary vocabulary)
    {
        var matches = new List<RuneshapingRewardMatch>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reward in rewards)
        {
            var match = MatchRewardName(reward, vocabulary);
            var key = match.MergeKey;
            if (seen.Add(key))
            {
                matches.Add(match);
            }
        }

        return matches;
    }

    private static RuneshapingRewardMatch MatchRewardName(
        RawReward reward,
        RuneshapingRewardVocabulary vocabulary)
    {
        var itemName = reward.ItemName;
        if (TryFindVocabularyName(itemName, vocabulary, out var known))
        {
            return RuneshapingRewardMatch.Exact(reward, known, PoeNinjaPrices.Normalize(itemName));
        }

        var repaired = ApplyExplicitRuneshapingRepairs(itemName);
        if (!repaired.Equals(itemName, StringComparison.OrdinalIgnoreCase) &&
            TryFindVocabularyName(repaired, vocabulary, out known))
        {
            return RuneshapingRewardMatch.Alias(reward, known, PoeNinjaPrices.Normalize(itemName));
        }

        var withoutLeadingJunk = Regex.Replace(repaired, @"^[A-Z]\s+(?=[A-Z])", string.Empty);
        if (!withoutLeadingJunk.Equals(repaired, StringComparison.OrdinalIgnoreCase) &&
            TryFindVocabularyName(withoutLeadingJunk, vocabulary, out known))
        {
            return RuneshapingRewardMatch.Alias(reward, known, PoeNinjaPrices.Normalize(itemName));
        }

        var withoutTrailingJunk = Regex.Replace(repaired, @"\s+[A-Z]$", string.Empty);
        if (!withoutTrailingJunk.Equals(repaired, StringComparison.OrdinalIgnoreCase) &&
            TryFindVocabularyName(withoutTrailingJunk, vocabulary, out known))
        {
            return RuneshapingRewardMatch.Alias(reward, known, PoeNinjaPrices.Normalize(itemName));
        }

        if (!repaired.EndsWith(" Rune", StringComparison.OrdinalIgnoreCase) &&
            TryFindVocabularyName(repaired + " Rune", vocabulary, out known))
        {
            return RuneshapingRewardMatch.Alias(reward, known, PoeNinjaPrices.Normalize(itemName));
        }

        var matchingName = !withoutLeadingJunk.Equals(repaired, StringComparison.OrdinalIgnoreCase)
            ? withoutLeadingJunk
            : repaired;

        if (TryFindUniquePrefixVocabularyName(matchingName, vocabulary, out known))
        {
            return RuneshapingRewardMatch.UniquePrefixCompletion(
                reward,
                known,
                matchingName,
                PoeNinjaPrices.Normalize(itemName));
        }

        var nearMatch = FindNearVocabularyName(matchingName, vocabulary);
        if (nearMatch.Accepted)
        {
            return RuneshapingRewardMatch.Near(
                reward,
                nearMatch.KnownName,
                PoeNinjaPrices.Normalize(itemName),
                nearMatch.Distance,
                nearMatch.SecondBestDistance,
                nearMatch.Candidates,
                nearMatch.RejectionReason);
        }

        if (TryFindAnchorVocabularyName(matchingName, vocabulary, out known, out var anchor))
        {
            return RuneshapingRewardMatch.AnchorMatch(
                reward,
                known,
                anchor,
                PoeNinjaPrices.Normalize(itemName),
                nearMatch.Candidates,
                nearMatch.RejectionReason);
        }

        var tinyFullMatch = FindTinyFullVocabularyName(matchingName, vocabulary);
        if (tinyFullMatch.Accepted)
        {
            return RuneshapingRewardMatch.Near(
                reward,
                tinyFullMatch.KnownName,
                PoeNinjaPrices.Normalize(itemName),
                tinyFullMatch.Distance,
                tinyFullMatch.SecondBestDistance,
                tinyFullMatch.Candidates,
                tinyFullMatch.RejectionReason);
        }

        return RuneshapingRewardMatch.Unmatched(
            reward,
            matchingName,
            PoeNinjaPrices.Normalize(itemName),
            tinyFullMatch.Candidates.Count > 0 ? tinyFullMatch.Candidates : nearMatch.Candidates,
            tinyFullMatch.RejectionReason.Length > 0 ? tinyFullMatch.RejectionReason : nearMatch.RejectionReason);
    }

    private static string ApplyExplicitRuneshapingRepairs(string itemName)
    {
        var repaired = Regex.Replace(itemName, @"\bJeweller\s+S\s+Orb\b", "Jeweller's Orb", RegexOptions.IgnoreCase);
        repaired = Regex.Replace(repaired, @"\bJewellers\s+Orb\b", "Jeweller's Orb", RegexOptions.IgnoreCase);
        repaired = Regex.Replace(repaired, @"\b(Blacksmith|Arcanist|Armourer|Jeweller)\s+S\s+", "$1's ", RegexOptions.IgnoreCase);
        repaired = Regex.Replace(repaired, @"^Warding Rune of Ical$", "Warding Rune of Protection", RegexOptions.IgnoreCase);
        repaired = Regex.Replace(repaired, @"\bof\s+[A-Z]\s+(?=[A-Z])", "of ", RegexOptions.IgnoreCase);
        repaired = Regex.Replace(
            repaired,
            @"^Uncut (?<kind>Skill|Spirit|Support) Gem Level (?<level>\d{1,2})$",
            "Uncut ${kind} Gem (Level ${level})",
            RegexOptions.IgnoreCase);
        repaired = Regex.Replace(repaired, @"\s+", " ").Trim();
        return repaired;
    }

    private static bool LooksLikeScrollableRewardPanel(Bitmap crop)
    {
        var stripLeft = Math.Max(0, crop.Width - 34);
        var stripRight = Math.Max(stripLeft + 1, crop.Width - 5);
        var top = Math.Min(crop.Height - 1, 120);
        var bottom = Math.Max(top + 1, crop.Height - 60);
        var rowsWithTrack = 0;

        for (var y = top; y < bottom; y++)
        {
            var darkPixels = 0;
            var neutralPixels = 0;
            for (var x = stripLeft; x < stripRight; x++)
            {
                var color = crop.GetPixel(x, y);
                var max = Math.Max(color.R, Math.Max(color.G, color.B));
                var min = Math.Min(color.R, Math.Min(color.G, color.B));
                var luminance = (int)(0.299 * color.R + 0.587 * color.G + 0.114 * color.B);

                if (luminance < 80)
                {
                    darkPixels++;
                }

                if (max - min < 48 && luminance is > 65 and < 190)
                {
                    neutralPixels++;
                }
            }

            if (darkPixels >= 3 && neutralPixels >= 5)
            {
                rowsWithTrack++;
            }
        }

        return rowsWithTrack > (bottom - top) * 0.22;
    }

    private static bool TryFindVocabularyName(
        string itemName,
        RuneshapingRewardVocabulary vocabulary,
        out string knownName)
    {
        var normalized = PoeNinjaPrices.Normalize(itemName);
        if (vocabulary.NamesByNormalizedKey.TryGetValue(normalized, out var match))
        {
            knownName = match;
            return true;
        }

        knownName = string.Empty;
        return false;
    }

    private static bool TryFindUniquePrefixVocabularyName(
        string itemName,
        RuneshapingRewardVocabulary vocabulary,
        out string knownName)
    {
        knownName = string.Empty;
        var normalized = PoeNinjaPrices.Normalize(itemName);
        if (!IsSpecificEnoughForPrefixCompletion(itemName, normalized, vocabulary))
        {
            return false;
        }

        var matches = vocabulary.CanonicalNames
            .Where(name => PoeNinjaPrices.Normalize(name).StartsWith(normalized + " ", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();

        if (matches.Length != 1)
        {
            return false;
        }

        knownName = matches[0];
        return true;
    }

    private static bool IsSpecificEnoughForPrefixCompletion(
        string itemName,
        string normalized,
        RuneshapingRewardVocabulary vocabulary)
    {
        if (normalized.Length < 10)
        {
            return false;
        }

        if (!NormalizedTokens(itemName).Any(token => token.Length >= 6))
        {
            return false;
        }

        return !TryFindKnownFamilyPrefixWithBadSuffix(itemName, vocabulary, out _);
    }

    private static NearKnownNameMatch FindNearVocabularyName(
        string itemName,
        RuneshapingRewardVocabulary vocabulary)
    {
        var normalized = PoeNinjaPrices.Normalize(itemName);
        if (normalized.Length < 8)
        {
            return NearKnownNameMatch.Rejected("input too short", []);
        }

        var inputTokens = NormalizedTokens(itemName);
        var candidates = vocabulary.MatchCandidates
            .Where(candidate => candidate.NormalizedName.Length >= 8)
            .Where(candidate => HasStrongTokenAnchor(inputTokens, candidate.Tokens))
            .Select(candidate => new NearCandidate(
                candidate.DisplayName,
                EditDistance(normalized, candidate.NormalizedName)))
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

        var candidateSummaries = candidates
            .Select(candidate => $"{candidate.Name}:{candidate.Distance}")
            .ToArray();
        var best = candidates.FirstOrDefault();
        if (best is null)
        {
            return NearKnownNameMatch.Rejected("no anchored candidates", candidateSummaries);
        }

        var maxDistance = normalized.Length <= 12 ? 1 : 2;
        if (best.Distance > maxDistance)
        {
            return NearKnownNameMatch.Rejected($"best distance {best.Distance} > {maxDistance}", candidateSummaries);
        }

        var secondBest = candidates.Skip(1).FirstOrDefault();
        if (secondBest is not null && secondBest.Distance - best.Distance < 2)
        {
            return NearKnownNameMatch.Rejected(
                $"ambiguous best {best.Distance}, second {secondBest.Distance}",
                candidateSummaries);
        }

        return NearKnownNameMatch.AcceptedMatch(best.Name, best.Distance, secondBest?.Distance, candidateSummaries);
    }

    private static NearKnownNameMatch FindTinyFullVocabularyName(
        string itemName,
        RuneshapingRewardVocabulary vocabulary)
    {
        var normalized = PoeNinjaPrices.Normalize(itemName);
        if (normalized.Length < 12)
        {
            return NearKnownNameMatch.Rejected("full vocabulary fallback input too short", []);
        }

        var candidates = vocabulary.MatchCandidates
            .Where(candidate => candidate.NormalizedName.Length >= 12)
            .Select(candidate => new NearCandidate(
                candidate.DisplayName,
                EditDistance(normalized, candidate.NormalizedName)))
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

        var candidateSummaries = candidates
            .Select(candidate => $"{candidate.Name}:{candidate.Distance}")
            .ToArray();
        var best = candidates.FirstOrDefault();
        if (best is null)
        {
            return NearKnownNameMatch.Rejected("no full vocabulary candidates", candidateSummaries);
        }

        if (best.Distance > 1)
        {
            return NearKnownNameMatch.Rejected($"full vocabulary best distance {best.Distance} > 1", candidateSummaries);
        }

        var secondBest = candidates.Skip(1).FirstOrDefault();
        if (secondBest is not null && secondBest.Distance - best.Distance < 2)
        {
            return NearKnownNameMatch.Rejected(
                $"full vocabulary ambiguous best {best.Distance}, second {secondBest.Distance}",
                candidateSummaries);
        }

        return NearKnownNameMatch.AcceptedMatch(best.Name, best.Distance, secondBest?.Distance, candidateSummaries);
    }

    private static IReadOnlyList<KnownNameCandidate> BuildKnownNameCandidates(
        IReadOnlyList<RuneshapingVocabularyItem> entries)
    {
        return entries
            .Select(entry => entry.CanonicalName.Trim())
            .Where(name => name.Length > 0)
            .Select(displayName => new KnownNameCandidate(
                displayName,
                PoeNinjaPrices.Normalize(displayName),
                NormalizedTokens(displayName)))
            .GroupBy(candidate => candidate.NormalizedName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(candidate => candidate.DisplayName.Contains('-', StringComparison.Ordinal))
                .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                .First())
            .ToArray();
    }

    private static bool HasStrongTokenAnchor(IReadOnlyList<string> inputTokens, IReadOnlyList<string> candidateTokens)
    {
        return inputTokens
            .Where(token => token.Length >= 4)
            .Any(token => candidateTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
    }

    private static bool TryFindAnchorVocabularyName(
        string itemName,
        RuneshapingRewardVocabulary vocabulary,
        out string knownName,
        out string anchor)
    {
        knownName = string.Empty;
        anchor = string.Empty;

        var inputFirstToken = NormalizedTokens(itemName).FirstOrDefault();
        if (inputFirstToken is null)
        {
            return false;
        }

        var anchorFamilies = new[]
        {
            new AnchorFamily("Blacksmith", ["BLACKSMITH", "BLACKSMITHS"]),
            new AnchorFamily("Arcanist", ["ARCANIST", "ARCANISTS"]),
            new AnchorFamily("Armourer", ["ARMOURER", "ARMOURERS"])
        };

        foreach (var family in anchorFamilies)
        {
            if (!family.Tokens.Contains(inputFirstToken, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidates = vocabulary.CanonicalNames
                .Where(candidate =>
                {
                    var candidateFirstToken = NormalizedTokens(candidate).FirstOrDefault();
                    return candidateFirstToken is not null &&
                        family.Tokens.Contains(candidateFirstToken, StringComparer.OrdinalIgnoreCase);
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (candidates.Length == 1)
            {
                knownName = candidates[0];
                anchor = family.Label;
                return true;
            }
        }

        return false;
    }

    private static RuneshapingRewardVocabulary LoadRuneshapingRewardVocabulary()
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "Data", "runeshaping_reward_vocabulary.json");
        if (!File.Exists(sourcePath))
        {
            return RuneshapingRewardVocabulary.Empty(sourcePath, "vocabulary file not found");
        }

        try
        {
            using var stream = File.OpenRead(sourcePath);
            var entries = JsonSerializer.Deserialize<List<RuneshapingVocabularyItem>>(stream, VocabularyJsonOptions) ?? [];
            var canonicalEntries = entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.CanonicalName))
                .ToArray();
            var namesByNormalizedKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in canonicalEntries)
            {
                AddVocabularyLookup(namesByNormalizedKey, entry.CanonicalName, entry.CanonicalName);

                foreach (var alias in entry.Aliases ?? [])
                {
                    AddVocabularyLookup(namesByNormalizedKey, alias, entry.CanonicalName);
                }

                if (!string.IsNullOrWhiteSpace(entry.NormalizedKey))
                {
                    namesByNormalizedKey.TryAdd(
                        NormalizeVocabularyKey(entry.NormalizedKey),
                        entry.CanonicalName);
                }
            }

            var canonicalNames = namesByNormalizedKey.Values
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new RuneshapingRewardVocabulary(
                canonicalNames,
                namesByNormalizedKey,
                BuildKnownNameCandidates(canonicalEntries),
                sourcePath,
                string.Empty);
        }
        catch (Exception ex)
        {
            return RuneshapingRewardVocabulary.Empty(sourcePath, ex.Message);
        }
    }

    private static void AddVocabularyLookup(
        Dictionary<string, string> namesByNormalizedKey,
        string sourceName,
        string canonicalName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return;
        }

        namesByNormalizedKey.TryAdd(PoeNinjaPrices.Normalize(sourceName), canonicalName);
    }

    private static string NormalizeVocabularyKey(string normalizedKey)
    {
        return PoeNinjaPrices.Normalize(Regex.Replace(normalizedKey, @"(?<!^)([A-Z])", " $1"));
    }

    private static string[] NormalizedTokens(string itemName)
    {
        return PoeNinjaPrices.Normalize(itemName)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string FormatMatchDebugLine(RuneshapingRewardMatch match, PoeNinjaPrices prices)
    {
        var priceLookupName = ResolveRuneshapingPriceLookupName(match.Reward.ItemName);
        var price = prices.TryGetValue(priceLookupName, match.Reward.Quantity);
        var canonical = match.Matched
            ? match.Reward.ItemName
            : "(unmatched)";
        var score = match.Distance is null
            ? string.Empty
            : $" distance={match.Distance}";
        var secondBest = match.SecondBestDistance is null
            ? string.Empty
            : $" secondBest={match.SecondBestDistance}";
        var anchor = string.IsNullOrWhiteSpace(match.AnchorLabel)
            ? string.Empty
            : $" acceptedByUniqueAnchor=true anchor={match.AnchorLabel}";
        var prefixCompletion = string.IsNullOrWhiteSpace(match.PrefixCompletion)
            ? string.Empty
            : $" prefix='{match.PrefixCompletion}'";
        var exact = match.Method.Equals("exact", StringComparison.OrdinalIgnoreCase)
            ? $" exact={match.Reward.ItemName}"
            : " exact=(none)";
        var nearMatchCandidates = match.NearMatchCandidates ?? [];
        var nearCandidates = nearMatchCandidates.Count == 0
            ? " nearCandidates=(none)"
            : $" nearCandidates={string.Join("; ", nearMatchCandidates)}";
        var nearRejection = string.IsNullOrWhiteSpace(match.NearMatchRejection) ||
            match.Method.Equals("anchor", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $" nearRejected='{match.NearMatchRejection}'";
        var priceLookup = priceLookupName.Equals(match.Reward.ItemName, StringComparison.OrdinalIgnoreCase)
            ? " priceLookup=(canonical)"
            : $" priceLookup='{priceLookupName}'";
        var priceStatus = price is null ? "missing" : "ok";
        return $"{match.ParsedReward.Quantity}x parsed='{match.ParsedReward.ItemName}' normalized='{match.NormalizedParsedName}' canonical='{canonical}' method={match.Method}{prefixCompletion}{exact}{anchor}{score}{secondBest}{nearCandidates}{nearRejection} mergeKey='{match.MergeKey}'{priceLookup} price={priceStatus}";
    }

    private static string FormatIgnoredRewardDebugLine(
        RuneshapingRewardMatch match,
        RuneshapingRewardVocabulary vocabulary)
    {
        var nearMatchCandidates = match.NearMatchCandidates ?? [];
        var nearCandidates = nearMatchCandidates.Count == 0
            ? "nearCandidates=(none)"
            : $"nearCandidates={string.Join("; ", nearMatchCandidates)}";
        var nearRejection = string.IsNullOrWhiteSpace(match.NearMatchRejection)
            ? "nearRejected=(none)"
            : $"nearRejected='{match.NearMatchRejection}'";
        return $"{match.ParsedReward.Quantity}x parsed='{match.ParsedReward.ItemName}' reason='{DescribeIgnoredReward(match, vocabulary)}' {nearCandidates} {nearRejection}";
    }

    private static string DescribeIgnoredReward(
        RuneshapingRewardMatch match,
        RuneshapingRewardVocabulary vocabulary)
    {
        var itemName = match.ParsedReward.ItemName;
        if (TryFindKnownFamilyPrefixWithBadSuffix(itemName, vocabulary, out var familyReason))
        {
            return familyReason;
        }

        if (LooksLikeIncompleteFamilyPrefix(itemName, vocabulary))
        {
            return "incomplete known reward family prefix";
        }

        return "unmatched low-confidence OCR reward";
    }

    private static bool TryFindKnownFamilyPrefixWithBadSuffix(
        string itemName,
        RuneshapingRewardVocabulary vocabulary,
        out string reason)
    {
        reason = string.Empty;
        var normalized = PoeNinjaPrices.Normalize(itemName);
        var prefix = vocabulary.CanonicalNames
            .Select(name => Regex.Match(PoeNinjaPrices.Normalize(name), @"^(?<prefix>.+ RUNE OF) (?<suffix>.+)$"))
            .Where(match => match.Success)
            .Select(match => match.Groups["prefix"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(candidatePrefix => normalized.StartsWith(candidatePrefix + " ", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidatePrefix => candidatePrefix.Length)
            .FirstOrDefault();

        if (prefix is null)
        {
            return false;
        }

        var suffix = normalized[(prefix.Length + 1)..].Trim();
        if (suffix.Length <= 3 || suffix.Any(char.IsDigit))
        {
            reason = $"known family prefix with malformed suffix prefix='{NormalizeDebugToken(prefix)}' suffix='{NormalizeDebugToken(suffix)}'";
            return true;
        }

        return false;
    }

    private static bool LooksLikeIncompleteFamilyPrefix(
        string itemName,
        RuneshapingRewardVocabulary vocabulary)
    {
        var normalized = PoeNinjaPrices.Normalize(itemName);
        if (!normalized.EndsWith(" RUNE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return vocabulary.CanonicalNames
            .Select(PoeNinjaPrices.Normalize)
            .Any(name => name.StartsWith(normalized + " OF ", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeDebugToken(string value)
    {
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
    }

    private static string ResolveRuneshapingPriceLookupName(string canonicalName)
    {
        return RuneshapingPriceLookupAliases.TryGetValue(canonicalName, out var alias)
            ? alias
            : canonicalName;
    }

    private static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private sealed record RuneshapingRewardMatch(
        RawReward ParsedReward,
        RawReward Reward,
        string Method,
        string? AnchorLabel = null,
        int? Distance = null,
        int? SecondBestDistance = null,
        string NormalizedParsedName = "",
        string PrefixCompletion = "",
        IReadOnlyList<string>? NearMatchCandidates = null,
        string NearMatchRejection = "")
    {
        public bool Matched => !Method.Equals("unmatched", StringComparison.OrdinalIgnoreCase);

        public string MergeKey => $"{Reward.Quantity}x {Reward.ItemName}";

        public static RuneshapingRewardMatch Exact(RawReward reward, string canonicalName, string normalizedParsedName)
        {
            return new RuneshapingRewardMatch(
                reward,
                reward with { ItemName = canonicalName },
                "exact",
                NormalizedParsedName: normalizedParsedName,
                NearMatchCandidates: []);
        }

        public static RuneshapingRewardMatch Alias(RawReward reward, string canonicalName, string normalizedParsedName)
        {
            return new RuneshapingRewardMatch(
                reward,
                reward with { ItemName = canonicalName },
                "alias",
                NormalizedParsedName: normalizedParsedName,
                NearMatchCandidates: []);
        }

        public static RuneshapingRewardMatch UniquePrefixCompletion(
            RawReward reward,
            string canonicalName,
            string prefix,
            string normalizedParsedName)
        {
            return new RuneshapingRewardMatch(
                reward,
                reward with { ItemName = canonicalName },
                "uniquePrefixCompletion",
                NormalizedParsedName: normalizedParsedName,
                PrefixCompletion: prefix,
                NearMatchCandidates: []);
        }

        public static RuneshapingRewardMatch Near(
            RawReward reward,
            string canonicalName,
            string normalizedParsedName,
            int distance,
            int? secondBestDistance,
            IReadOnlyList<string> nearMatchCandidates,
            string nearMatchRejection)
        {
            return new RuneshapingRewardMatch(
                reward,
                reward with { ItemName = canonicalName },
                "near",
                Distance: distance,
                SecondBestDistance: secondBestDistance,
                NormalizedParsedName: normalizedParsedName,
                NearMatchCandidates: nearMatchCandidates,
                NearMatchRejection: nearMatchRejection);
        }

        public static RuneshapingRewardMatch AnchorMatch(
            RawReward reward,
            string canonicalName,
            string anchor,
            string normalizedParsedName,
            IReadOnlyList<string> nearMatchCandidates,
            string nearMatchRejection)
        {
            return new RuneshapingRewardMatch(
                reward,
                reward with { ItemName = canonicalName },
                "anchor",
                anchor,
                NormalizedParsedName: normalizedParsedName,
                NearMatchCandidates: nearMatchCandidates,
                NearMatchRejection: nearMatchRejection);
        }

        public static RuneshapingRewardMatch Unmatched(
            RawReward reward,
            string repairedName,
            string normalizedParsedName,
            IReadOnlyList<string> nearMatchCandidates,
            string nearMatchRejection)
        {
            return new RuneshapingRewardMatch(
                reward,
                reward with { ItemName = repairedName },
                "unmatched",
                NormalizedParsedName: normalizedParsedName,
                NearMatchCandidates: nearMatchCandidates,
                NearMatchRejection: nearMatchRejection);
        }
    }

    private sealed record AnchorFamily(string Label, string[] Tokens);

    private sealed record RewardParseCandidate(
        string Source,
        string Text,
        string StitchKey = "",
        string RepairReason = "",
        IReadOnlyList<string>? SourceLines = null);

    private sealed record RowOcrRetryAttempt(
        int LineNumber,
        string SourceText,
        string SourceBounds,
        Rectangle RetryCropBounds,
        string Mode,
        string Backend,
        string RawText,
        bool Accepted,
        RawReward? Reward,
        string Method,
        string RejectionReason);

    private sealed record RuneshapingVocabularyItem(
        string CanonicalName,
        string NormalizedKey,
        string[]? Aliases);

    private sealed record RuneshapingRewardVocabulary(
        IReadOnlyList<string> CanonicalNames,
        IReadOnlyDictionary<string, string> NamesByNormalizedKey,
        IReadOnlyList<KnownNameCandidate> MatchCandidates,
        string SourcePath,
        string LoadError)
    {
        public static RuneshapingRewardVocabulary Empty(string sourcePath, string loadError)
        {
            return new RuneshapingRewardVocabulary(
                [],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                [],
                sourcePath,
                loadError);
        }
    }

    private sealed record KnownNameCandidate(string DisplayName, string NormalizedName, IReadOnlyList<string> Tokens);

    private sealed record NearCandidate(string Name, int Distance);

    private sealed record NearKnownNameMatch(
        bool Accepted,
        string KnownName,
        int Distance,
        int? SecondBestDistance,
        IReadOnlyList<string> Candidates,
        string RejectionReason)
    {
        public static NearKnownNameMatch AcceptedMatch(
            string knownName,
            int distance,
            int? secondBestDistance,
            IReadOnlyList<string> candidates)
        {
            return new NearKnownNameMatch(true, knownName, distance, secondBestDistance, candidates, string.Empty);
        }

        public static NearKnownNameMatch Rejected(string reason, IReadOnlyList<string> candidates)
        {
            return new NearKnownNameMatch(false, string.Empty, 0, null, candidates, reason);
        }
    }

    private sealed record RuneshapingParserTestCase(
        string Name,
        string RawText,
        IReadOnlyList<string> ExpectedAccepted,
        IReadOnlyList<string> ExpectedIgnored);

    private static Bitmap PrepareForOcr(Bitmap input)
    {
        var output = new Bitmap(input.Width * 2, input.Height * 2, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(output);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(input, 0, 0, output.Width, output.Height);

        for (var y = 0; y < output.Height; y++)
        {
            for (var x = 0; x < output.Width; x++)
            {
                var color = output.GetPixel(x, y);
                var luminance = (int)(0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
                var neutralInk = Math.Abs(color.R - color.G) < 55 && Math.Abs(color.G - color.B) < 55;
                var likelyText = luminance < 120 && neutralInk;
                output.SetPixel(x, y, likelyText ? Color.Black : Color.White);
            }
        }

        return output;
    }

    private static Rectangle ResolveCropRegion(Size screenshotSize)
    {
        if (screenshotSize.Width == 3840 && screenshotSize.Height == 2160)
        {
            return Crop3840x2160;
        }

        var scaleX = screenshotSize.Width / 3840d;
        var scaleY = screenshotSize.Height / 2160d;
        return new Rectangle(
            (int)Math.Round(Crop3840x2160.X * scaleX),
            (int)Math.Round(Crop3840x2160.Y * scaleY),
            (int)Math.Round(Crop3840x2160.Width * scaleX),
            (int)Math.Round(Crop3840x2160.Height * scaleY));
    }

    private static Screen SelectTargetScreen()
    {
        if (TryFindPathOfExileScreen(out var poeScreen))
        {
            return poeScreen;
        }

        return Screen.AllScreens
            .OrderByDescending(screen => screen.Bounds.Width == 3840 && screen.Bounds.Height == 2160)
            .ThenByDescending(screen => screen.Bounds.Width * screen.Bounds.Height)
            .FirstOrDefault() ?? Screen.PrimaryScreen!;
    }

    private static bool TryFindPathOfExileScreen(out Screen screen)
    {
        screen = Screen.PrimaryScreen!;
        var matches = new List<IntPtr>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            var title = GetWindowTitle(handle);
            GetWindowThreadProcessId(handle, out var processId);
            var processName = TryGetProcessName(processId);

            if (LooksLikePathOfExile(title, processName))
            {
                matches.Add(handle);
            }

            return true;
        }, IntPtr.Zero);

        foreach (var handle in matches)
        {
            if (!GetWindowRect(handle, out var rect))
            {
                continue;
            }

            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (bounds.Width < 1000 || bounds.Height < 700)
            {
                continue;
            }

            screen = Screen.FromRectangle(bounds);
            return true;
        }

        return false;
    }

    private static bool LooksLikePathOfExile(string title, string processName)
    {
        return title.Contains("Path of Exile", StringComparison.OrdinalIgnoreCase) ||
               processName.Contains("PathOfExile", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string TryGetProcessName(uint processId)
    {
        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private string CreateRuneshapingDebugImageDirectory()
    {
        var parent = Path.Combine(_debugDirectory, "runeshaping");
        Directory.CreateDirectory(parent);

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var path = Path.Combine(parent, timestamp);
        for (var suffix = 2; Directory.Exists(path); suffix++)
        {
            path = Path.Combine(parent, $"{timestamp}-{suffix}");
        }

        Directory.CreateDirectory(path);
        return path;
    }

    private static Bitmap CaptureScreen(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static void SaveBitmap(Bitmap bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static async Task<string> EnsureTessDataAsync(string tessDataDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(tessDataDirectory);
        var trainedDataPath = Path.Combine(tessDataDirectory, "eng.traineddata");
        if (File.Exists(trainedDataPath))
        {
            return tessDataDirectory;
        }

        using var client = new HttpClient();
        var bytes = await client.GetByteArrayAsync(
            "https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata",
            cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(trainedDataPath, bytes, cancellationToken).ConfigureAwait(false);
        return tessDataDirectory;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}
