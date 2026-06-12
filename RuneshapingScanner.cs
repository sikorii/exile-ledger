using System.Drawing.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Tesseract;

namespace Poe2PriceChecker;

internal sealed class RuneshapingScanner
{
    private static readonly Rectangle Crop3840x2160 = new(407, 300, 680, 1080);
    private static readonly Regex RewardLine = new(
        @"(?<qty>\d+)\s*[xX]\s+(?<name>[A-Za-z][A-Za-z0-9' -]{2,})",
        RegexOptions.Compiled);

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

    private async Task<ScanResult> ScanBitmapAsync(
        Bitmap screenshot,
        Rectangle screenBounds,
        bool mergeWithRecentRuneshapingScans,
        CancellationToken cancellationToken)
    {
        var cropRegion = ResolveCropRegion(screenshot.Size);
        using var crop = screenshot.Clone(cropRegion, screenshot.PixelFormat);
        var panelLooksScrollable = LooksLikeScrollableRewardPanel(crop);
        SaveBitmap(crop, Path.Combine(_debugDirectory, "runeshaping-crop.png"));

        using var processed = PrepareForOcr(crop);
        var processedPath = Path.Combine(_debugDirectory, "runeshaping-ocr.png");
        SaveBitmap(processed, processedPath);

        var tessData = await EnsureTessDataAsync(Path.Combine(AppContext.BaseDirectory, "tessdata"), cancellationToken).ConfigureAwait(false);
        var rawText = RunOcr(processedPath, tessData);
        File.WriteAllText(Path.Combine(_debugDirectory, "runeshaping-ocr.txt"), rawText);
        var rewards = ParseRewards(rawText);

        if (_cachedPrices is null || DateTimeOffset.UtcNow - _lastPriceRefresh > TimeSpan.FromMinutes(30))
        {
            await RefreshPricesAsync(cancellationToken).ConfigureAwait(false);
        }

        var notes = new List<string>();
        var repairedRewards = RepairRewards(rewards, _cachedPrices!.KnownItemNames);
        var rewardsForPricing = mergeWithRecentRuneshapingScans
            ? MergeRewardsForCurrentEncounter(repairedRewards, notes)
            : repairedRewards;

        if (panelLooksScrollable || repairedRewards.Count >= 8)
        {
            notes.Add("Reward list may be scrollable. Scroll the panel and press F8 again to merge hidden rows.");
        }

        var priced = new List<RewardChoice>();
        var unpriced = new List<string>();
        foreach (var reward in rewardsForPricing)
        {
            var value = _cachedPrices.TryGetValue(reward.ItemName, reward.Quantity);
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

        var colored = AssignColors(priced);
        File.WriteAllLines(
            Path.Combine(_debugDirectory, "runeshaping-debug.txt"),
            new[] { "Raw OCR:", rawText, string.Empty, "Parsed rewards:" }
                .Concat(rewards.Select(reward => $"{reward.Quantity}x {reward.ItemName}"))
                .Concat(new[] { string.Empty, "Repaired rewards:" })
                .Concat(repairedRewards.Select(reward => $"{reward.Quantity}x {reward.ItemName}"))
                .Concat(new[] { string.Empty, "Merged rewards:" })
                .Concat(rewardsForPricing.Select(reward => $"{reward.Quantity}x {reward.ItemName}"))
                .Concat(new[] { string.Empty, "Notes:" })
                .Concat(notes.DefaultIfEmpty("(none)"))
                .Concat(new[] { string.Empty, "Unpriced:" })
                .Concat(unpriced.DefaultIfEmpty("(none)"))
                .Concat(new[] { string.Empty, "Unpriced diagnostics:" })
                .Concat(unpriced.Count == 0
                    ? ["(none)"]
                    : unpriced.Select(rewardText =>
                    {
                        var match = Regex.Match(rewardText, @"^\d+x\s+(?<name>.+)$");
                        var name = match.Success ? match.Groups["name"].Value : rewardText;
                        return _cachedPrices.DiagnoseMissing(name).ToDebugString();
                    })));
        return new ScanResult(colored, unpriced, notes, rawText, cropRegion, screenBounds);
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
            notes.Add($"Merged {_mergedRewards.Count} rewards from this Runeshaping panel. Close the overlay x to reset.");
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

    private static IReadOnlyList<RawReward> ParseRewards(string rawText)
    {
        var rewards = new List<RawReward>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in RewardLine.Matches(rawText))
        {
            if (!int.TryParse(match.Groups["qty"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) ||
                quantity <= 0)
            {
                continue;
            }

            var itemName = NormalizeItemName(match.Groups["name"].Value);
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

        return rewards;
    }

    private static string NormalizeItemName(string value)
    {
        var cleaned = Regex.Replace(value, @"\s+", " ").Trim(' ', '-', '\'');
        var titled = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cleaned.ToLowerInvariant());
        return Regex.Replace(titled, @"\b(Of|The|And)\b", m => m.Value.ToLowerInvariant());
    }

    private static IReadOnlyList<RawReward> RepairRewards(
        IReadOnlyList<RawReward> rewards,
        IReadOnlyList<string> knownItemNames)
    {
        var repaired = new List<RawReward>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reward in rewards)
        {
            var itemName = RepairItemName(reward.ItemName, knownItemNames);
            var key = $"{reward.Quantity}x {itemName}";
            if (seen.Add(key))
            {
                repaired.Add(reward with { ItemName = itemName });
            }
        }

        return repaired;
    }

    private static string RepairItemName(string itemName, IReadOnlyList<string> knownItemNames)
    {
        var repaired = ApplyExplicitRuneshapingRepairs(itemName);
        if (TryFindKnownName(repaired, knownItemNames, out var known))
        {
            return known;
        }

        var withoutLeadingJunk = Regex.Replace(repaired, @"^[A-Z]\s+(?=[A-Z])", string.Empty);
        if (!withoutLeadingJunk.Equals(repaired, StringComparison.OrdinalIgnoreCase) &&
            TryFindKnownName(withoutLeadingJunk, knownItemNames, out known))
        {
            return known;
        }

        var withoutTrailingJunk = Regex.Replace(repaired, @"\s+[A-Z]$", string.Empty);
        if (!withoutTrailingJunk.Equals(repaired, StringComparison.OrdinalIgnoreCase) &&
            TryFindKnownName(withoutTrailingJunk, knownItemNames, out known))
        {
            return known;
        }

        if (!repaired.EndsWith(" Rune", StringComparison.OrdinalIgnoreCase) &&
            TryFindKnownName(repaired + " Rune", knownItemNames, out known))
        {
            return known;
        }

        if (TryFuzzyKnownName(repaired, knownItemNames, out known))
        {
            return known;
        }

        return !withoutLeadingJunk.Equals(repaired, StringComparison.OrdinalIgnoreCase)
            ? withoutLeadingJunk
            : repaired;
    }

    private static string ApplyExplicitRuneshapingRepairs(string itemName)
    {
        var repaired = Regex.Replace(itemName, @"\bJeweller\s+S\s+Orb\b", "Jeweller's Orb", RegexOptions.IgnoreCase);
        repaired = Regex.Replace(repaired, @"\bJewellers\s+Orb\b", "Jeweller's Orb", RegexOptions.IgnoreCase);
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

    private static bool TryFindKnownName(string itemName, IReadOnlyList<string> knownItemNames, out string knownName)
    {
        var normalized = PoeNinjaPrices.Normalize(itemName);
        knownName = knownItemNames.FirstOrDefault(candidate =>
            !candidate.Contains('-', StringComparison.Ordinal) &&
            PoeNinjaPrices.Normalize(candidate).Equals(normalized, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return knownName.Length > 0;
    }

    private static bool TryFuzzyKnownName(string itemName, IReadOnlyList<string> knownItemNames, out string knownName)
    {
        knownName = string.Empty;
        var normalized = PoeNinjaPrices.Normalize(itemName);
        if (normalized.Length < 8)
        {
            return false;
        }

        var candidates = knownItemNames
            .Where(candidate => !candidate.Contains('-', StringComparison.Ordinal))
            .Where(candidate => CandidateFamilyMatches(itemName, candidate))
            .Select(candidate => new
            {
                Name = candidate,
                Distance = EditDistance(normalized, PoeNinjaPrices.Normalize(candidate))
            })
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Name.Length)
            .ToArray();

        var best = candidates.FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        var threshold = itemName.Contains("Warding Rune of", StringComparison.OrdinalIgnoreCase)
            ? 7
            : Math.Max(2, (int)Math.Floor(normalized.Length * 0.18));
        if (best.Distance > threshold)
        {
            return false;
        }

        knownName = best.Name;
        return true;
    }

    private static bool CandidateFamilyMatches(string itemName, string candidate)
    {
        if (itemName.Contains("Rune", StringComparison.OrdinalIgnoreCase))
        {
            if (!candidate.Contains("Rune", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var itemPrefix = itemName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var candidatePrefix = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return itemPrefix is null ||
                candidatePrefix is null ||
                itemPrefix.Equals(candidatePrefix, StringComparison.OrdinalIgnoreCase) ||
                itemName.Contains(" Rune", StringComparison.OrdinalIgnoreCase);
        }

        if (itemName.Contains("Jeweller", StringComparison.OrdinalIgnoreCase))
        {
            return candidate.Contains("Jeweller", StringComparison.OrdinalIgnoreCase);
        }

        if (itemName.Contains("Uncut", StringComparison.OrdinalIgnoreCase))
        {
            return candidate.Contains("Uncut", StringComparison.OrdinalIgnoreCase);
        }

        return true;
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

    private static string RunOcr(string imagePath, string tessDataDirectory)
    {
        using var engine = new TesseractEngine(tessDataDirectory, "eng", EngineMode.Default);
        engine.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789' -x");
        engine.DefaultPageSegMode = PageSegMode.SparseText;

        using var pix = Pix.LoadFromFile(imagePath);
        using var page = engine.Process(pix);
        return page.GetText();
    }

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

    private static Bitmap CaptureScreen(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static void SaveBitmap(Bitmap bitmap, string path)
    {
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
