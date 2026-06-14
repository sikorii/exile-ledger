using System.Globalization;
using System.Text.RegularExpressions;

namespace Poe2PriceChecker;

internal sealed class AugmentRuneScanner
{
    private static readonly Rectangle StashCropRegion = StashLayoutProfile.Folder.DisplayCropRegion;

    private readonly string _debugDirectory;
    private readonly CurrencyMappingStore _mappingStore;
    private PoeNinjaPrices? _cachedPrices;
    private DateTimeOffset _lastPriceRefresh = DateTimeOffset.MinValue;

    public AugmentRuneScanner(string debugDirectory, CurrencyMappingStore mappingStore)
    {
        _debugDirectory = debugDirectory;
        _mappingStore = mappingStore;
        Directory.CreateDirectory(_debugDirectory);
    }

    public void SetSlot(int slotIndex, string itemName, int? countOverride)
    {
        _mappingStore.SetName(slotIndex, itemName);
        _mappingStore.SetCountOverride(slotIndex, countOverride);
    }

    public async Task RefreshPricesAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        _cachedPrices = await PoeNinjaPrices.FetchAsync(cancellationToken, forceRefresh).ConfigureAwait(false);
        _lastPriceRefresh = DateTimeOffset.UtcNow;
    }

    public async Task<RuneScanResult> RecalculateValuesAsync(RuneScanResult result, CancellationToken cancellationToken)
    {
        await EnsurePricesAsync(cancellationToken).ConfigureAwait(false);
        return ScanValueRecalculator.Recalculate(
            result,
            _cachedPrices!,
            (stacks, prices) => BuildUpgradeSuggestions(stacks, prices)
                .Where(suggestion => suggestion.IsProfitable)
                .OrderByDescending(suggestion => suggestion.IsProfitable)
                .ThenByDescending(suggestion => suggestion.ProfitExalts)
                .Take(20)
                .ToArray());
    }

    public async Task<RuneScanResult> ScanScreenAsync(CancellationToken cancellationToken, StashLayoutProfile? layout = null)
    {
        var screen = ScreenCaptureService.SelectPoeScreen();
        using var screenshot = ScreenCaptureService.CaptureScreen(screen.Bounds);
        return await ScanBitmapAsync(screenshot, screen.Bounds, cancellationToken, layout ?? StashLayoutProfile.Folder).ConfigureAwait(false);
    }

    public async Task<RuneScanResult> ScanFileAsync(string screenshotPath, CancellationToken cancellationToken, StashLayoutProfile? layout = null)
    {
        using var screenshot = CurrencyScanner.LoadBitmapWithoutFileLock(screenshotPath);
        return await ScanBitmapAsync(screenshot, new Rectangle(0, 0, screenshot.Width, screenshot.Height), cancellationToken, layout ?? StashLayoutProfile.Folder).ConfigureAwait(false);
    }

    private async Task<RuneScanResult> ScanBitmapAsync(Bitmap screenshot, Rectangle screenBounds, CancellationToken cancellationToken, StashLayoutProfile layout)
    {
        await EnsurePricesAsync(cancellationToken).ConfigureAwait(false);
        var mapper = StashCoordinateMapper.FromScreenshotSize(screenshot.Size);
        var actualLayout = mapper.ScaleLayoutFromBase(layout);

        CurrencyScanner.SaveBitmap(screenshot, Path.Combine(_debugDirectory, "runes-fullscreen.png"));
        var stashCropPath = Path.Combine(_debugDirectory, "runes-stash-crop.png");
        using var stashCrop = screenshot.Clone(actualLayout.DisplayCropRegion, screenshot.PixelFormat);
        CurrencyScanner.SaveBitmap(stashCrop, stashCropPath);

        var tessData = await CurrencyScanner.EnsureTessDataAsync(Path.Combine(AppContext.BaseDirectory, "tessdata"), cancellationToken).ConfigureAwait(false);
        var scanId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var stacks = new List<RuneStack>();
        var detections = new List<RuneSlotDetection>();
        var countDebugLines = new List<string>();
        var knownOccupied = 0;
        var unknownOccupied = 0;

        for (var slotIndex = 0; slotIndex < RuneSlotMap.Slots.Length; slotIndex++)
        {
            var slot = RuneSlotMap.Slots[slotIndex];
            var scanSlot = slot with { Bounds = mapper.OffsetAndScaleRectFromBase(slot.Bounds, layout.SlotOffset) };
            var itemName = _mappingStore.GetName(slotIndex, slot.ItemName);
            var countOverride = _mappingStore.GetCountOverride(slotIndex);
            if (countOverride == 0)
            {
                countDebugLines.Add($"Slot {slotIndex,2} {itemName ?? "(mapped slot)"}: forced empty override x0");
                detections.Add(BuildDetection(slotIndex, scanSlot, actualLayout, false, itemName, 0, null, null));
                continue;
            }

            var occupied =
                StashSlotVisuals.HasVisibleQuantityMarker(screenshot, scanSlot.Bounds) ||
                (itemName is not null && StashSlotVisuals.HasRuneBodySignal(screenshot, scanSlot.Bounds)) ||
                countOverride is not null;
            if (!occupied)
            {
                if (itemName is not null || countOverride is not null)
                {
                    countDebugLines.Add(
                        $"Slot {slotIndex,2} {itemName ?? "(mapped slot)"}: no visible stack count, skipped" +
                        (countOverride is null ? string.Empty : $" override x{countOverride} ignored"));
                }

                detections.Add(BuildDetection(slotIndex, scanSlot, actualLayout, false, itemName, 0, null, null));
                continue;
            }

            if (itemName is null)
            {
                unknownOccupied++;
                var unknownQuantityRead = StackCountReader.ReadRuneQuantity(
                    screenshot,
                    scanSlot.Bounds,
                    tessData,
                    new StackCountReadOptions(_debugDirectory, "runes", slotIndex, scanId, mapper.Profile.ScaleX));
                var unknownQuantity = countOverride ?? unknownQuantityRead.Quantity;
                var unknownTrainingStatus = CountTrainingHelpers.TrySaveFromOverride(
                    screenshot,
                    scanSlot.Bounds,
                    countOverride,
                    "runes",
                    slotIndex,
                    _debugDirectory);
                countDebugLines.Add(
                    $"Slot {slotIndex,2} (unknown): read x{unknownQuantityRead.Quantity}" +
                    (countOverride is null ? string.Empty : $" override x{countOverride}") +
                    (unknownTrainingStatus.Length == 0 ? string.Empty : $" ({unknownTrainingStatus})") +
                    $" ({unknownQuantityRead.DebugText})");
                detections.Add(BuildDetection(slotIndex, scanSlot, actualLayout, true, null, unknownQuantity, null, null, unknownQuantityRead));
                continue;
            }

            knownOccupied++;
            var quantityRead = StackCountReader.ReadRuneQuantity(
                screenshot,
                scanSlot.Bounds,
                tessData,
                new StackCountReadOptions(_debugDirectory, "runes", slotIndex, scanId, mapper.Profile.ScaleX));
            var quantity = countOverride ?? quantityRead.Quantity;
            var knownTrainingStatus = CountTrainingHelpers.TrySaveFromOverride(
                screenshot,
                scanSlot.Bounds,
                countOverride,
                "runes",
                slotIndex,
                _debugDirectory);
            countDebugLines.Add(
                $"Slot {slotIndex,2} {itemName}: x{quantity} read x{quantityRead.Quantity}" +
                (countOverride is null ? string.Empty : $" override x{countOverride}") +
                (knownTrainingStatus.Length == 0 ? string.Empty : $" ({knownTrainingStatus})") +
                $" ({quantityRead.DebugText})");

            var value = _cachedPrices!.TryGetValue(itemName, quantity);
            if (value is null)
            {
                unknownOccupied++;
                countDebugLines.Add("  " + _cachedPrices.DiagnoseMissing(itemName, FixedStashScannerProfiles.AugmentRunes.PriceCategories).ToDebugString());
                detections.Add(BuildDetection(slotIndex, scanSlot, actualLayout, true, itemName, quantity, null, null, quantityRead));
                continue;
            }

            stacks.Add(new RuneStack(itemName, quantity, value.Exalts, value.Divines));
            detections.Add(BuildDetection(slotIndex, scanSlot, actualLayout, true, itemName, quantity, value.Exalts, value.Divines, quantityRead));
        }

        var totalExalts = stacks.Sum(stack => stack.Exalts);
        var totalDivines = stacks.Sum(stack => stack.Divines);
        var topStacks = stacks
            .OrderByDescending(stack => stack.Exalts)
            .Take(10)
            .ToArray();
        var upgradeSuggestions = BuildUpgradeSuggestions(stacks, _cachedPrices!)
            .Where(suggestion => suggestion.IsProfitable)
            .OrderByDescending(suggestion => suggestion.IsProfitable)
            .ThenByDescending(suggestion => suggestion.ProfitExalts)
            .Take(20)
            .ToArray();
        var priceSummary = _cachedPrices!.CacheSummary;

        File.WriteAllLines(
            Path.Combine(_debugDirectory, "runes-debug.txt"),
            stacks.OrderByDescending(stack => stack.Exalts)
                .Select(stack => $"{stack.ItemName} x{stack.Quantity} = {stack.Exalts:0.##} ex / {stack.Divines:0.####} div")
                .Concat([
                    $"Price cache: {priceSummary.ItemCount} items fetched {priceSummary.FetchedUtc:O}",
                    $"Categories: {string.Join(", ", priceSummary.FetchedCategories)}",
                    $"Known occupied: {knownOccupied}",
                    $"Unknown occupied: {unknownOccupied}",
                    string.Empty,
                    "Upgrade Suggestions:"
                ])
                .Concat(upgradeSuggestions.Select(FormatSuggestion))
                .Concat([string.Empty, "Count reads:"])
                .Concat(countDebugLines));

        return new RuneScanResult(
            topStacks,
            upgradeSuggestions,
            totalExalts,
            totalDivines,
            knownOccupied,
            unknownOccupied,
            screenBounds,
            stashCropPath,
            detections);
    }

    private RuneSlotDetection BuildDetection(
        int slotIndex,
        RuneSlot slot,
        StashLayoutProfile layout,
        bool occupied,
        string? itemName,
        int? quantity,
        decimal? exalts,
        decimal? divines,
        QuantityReadResult? quantityRead = null)
    {
        var cropBounds = new Rectangle(
            slot.Bounds.X - layout.DisplayCropRegion.X,
            slot.Bounds.Y - layout.DisplayCropRegion.Y,
            slot.Bounds.Width,
            slot.Bounds.Height);
        var overlayBounds = FixedStashSlot.Inset(slot.Bounds, FixedStashScannerProfiles.AugmentRuneOverlayInset);
        var overlayCropBounds = new Rectangle(
            overlayBounds.X - layout.DisplayCropRegion.X,
            overlayBounds.Y - layout.DisplayCropRegion.Y,
            overlayBounds.Width,
            overlayBounds.Height);

        return new RuneSlotDetection(
            slotIndex,
            cropBounds,
            occupied,
            itemName,
            quantity,
            exalts,
            divines,
            _mappingStore.IsCustomMapped(slotIndex),
            _mappingStore.IsCountOverridden(slotIndex),
            quantityRead?.Confidence ?? (occupied ? 0 : 1),
            quantityRead?.Method ?? "unknown",
            overlayCropBounds);
    }

    private async Task EnsurePricesAsync(CancellationToken cancellationToken)
    {
        if (_cachedPrices is null || DateTimeOffset.UtcNow - _lastPriceRefresh > TimeSpan.FromMinutes(30))
        {
            await RefreshPricesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static IEnumerable<RuneUpgradeSuggestion> BuildUpgradeSuggestions(
        IReadOnlyList<RuneStack> stacks,
        PoeNinjaPrices prices)
    {
        foreach (var stack in stacks)
        {
            if (stack.Quantity < 3 || !TryGetNextTier(stack.ItemName, out var targetName))
            {
                continue;
            }

            var fromUnit = prices.TryGetValue(stack.ItemName, 1);
            var toUnit = prices.TryGetValue(targetName, 1);
            if (fromUnit is null || toUnit is null)
            {
                continue;
            }

            var upgradeCount = stack.Quantity / 3;
            var costExalts = fromUnit.Exalts * 3 * upgradeCount;
            var outputExalts = toUnit.Exalts * upgradeCount;
            var costDivines = fromUnit.Divines * 3 * upgradeCount;
            var outputDivines = toUnit.Divines * upgradeCount;

            yield return new RuneUpgradeSuggestion(
                stack.ItemName,
                targetName,
                stack.Quantity,
                upgradeCount,
                costExalts,
                outputExalts,
                outputExalts - costExalts,
                costDivines,
                outputDivines,
                outputDivines - costDivines);
        }
    }

    private static bool TryGetNextTier(string itemName, out string targetName)
    {
        targetName = string.Empty;
        var normalized = Regex.Replace(itemName.Trim(), @"\s+", " ");

        var lesserMatch = Regex.Match(normalized, @"^Lesser (?<family>.+ Rune)$", RegexOptions.IgnoreCase);
        if (lesserMatch.Success)
        {
            targetName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lesserMatch.Groups["family"].Value.ToLowerInvariant());
            return true;
        }

        if (Regex.IsMatch(normalized, @"^(Ancient|Greater|Perfect|Warding|Countess|Courtesan|Craiceann|Farrul|Fenumus|Hedgewitch|Lady|Rune|Saqawal|Thane|The Greatwolf)", RegexOptions.IgnoreCase))
        {
            return false;
        }

        if (!normalized.EndsWith(" Rune", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        targetName = "Greater " + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
        return true;
    }

    private static string FormatSuggestion(RuneUpgradeSuggestion suggestion)
    {
        var label = suggestion.IsProfitable ? "UPGRADE" : "SKIP";
        return $"{label}: {suggestion.UpgradeCount}x {suggestion.FromItemName} -> {suggestion.ToItemName}; profit {suggestion.ProfitExalts:+0.##;-0.##;0} ex / {suggestion.ProfitDivines:+0.####;-0.####;0} div";
    }
}

internal sealed record RuneSlot(Rectangle Bounds, string? ItemName);

internal static class RuneSlotMap
{
    // First-pass 3840x2160 Aug(rune) tab map. Names start blank so bad icon guesses do not create bad upgrade advice.
    public static readonly RuneSlot[] Slots =
    [
        // Main visible rune families: 5 rows x 3 tier columns x 3 panels.
        new(new Rectangle(68, 468, 112, 112), null),
        new(new Rectangle(188, 468, 112, 112), null),
        new(new Rectangle(309, 468, 112, 112), null),
        new(new Rectangle(488, 468, 112, 112), null),
        new(new Rectangle(608, 468, 112, 112), null),
        new(new Rectangle(729, 468, 112, 112), null),
        new(new Rectangle(905, 468, 112, 112), null),
        new(new Rectangle(1025, 468, 112, 112), null),
        new(new Rectangle(1145, 468, 112, 112), null),

        new(new Rectangle(68, 603, 112, 112), null),
        new(new Rectangle(188, 603, 112, 112), null),
        new(new Rectangle(309, 603, 112, 112), null),
        new(new Rectangle(488, 603, 112, 112), null),
        new(new Rectangle(608, 603, 112, 112), null),
        new(new Rectangle(729, 603, 112, 112), null),
        new(new Rectangle(905, 603, 112, 112), null),
        new(new Rectangle(1025, 603, 112, 112), null),
        new(new Rectangle(1145, 603, 112, 112), null),

        new(new Rectangle(68, 738, 112, 112), null),
        new(new Rectangle(188, 738, 112, 112), null),
        new(new Rectangle(309, 738, 112, 112), null),
        new(new Rectangle(488, 738, 112, 112), null),
        new(new Rectangle(608, 738, 112, 112), null),
        new(new Rectangle(729, 738, 112, 112), null),
        new(new Rectangle(905, 738, 112, 112), null),
        new(new Rectangle(1025, 738, 112, 112), null),
        new(new Rectangle(1145, 738, 112, 112), null),

        new(new Rectangle(68, 873, 112, 112), null),
        new(new Rectangle(188, 873, 112, 112), null),
        new(new Rectangle(309, 873, 112, 112), null),
        new(new Rectangle(488, 873, 112, 112), null),
        new(new Rectangle(608, 873, 112, 112), null),
        new(new Rectangle(729, 873, 112, 112), null),
        new(new Rectangle(905, 873, 112, 112), null),
        new(new Rectangle(1025, 873, 112, 112), null),
        new(new Rectangle(1145, 873, 112, 112), null),

        new(new Rectangle(68, 1008, 112, 112), null),
        new(new Rectangle(188, 1008, 112, 112), null),
        new(new Rectangle(309, 1008, 112, 112), null),
        new(new Rectangle(488, 1008, 112, 112), null),
        new(new Rectangle(608, 1008, 112, 112), null),
        new(new Rectangle(729, 1008, 112, 112), null),
        new(new Rectangle(905, 1008, 112, 112), null),
        new(new Rectangle(1025, 1008, 112, 112), null),
        new(new Rectangle(1145, 1008, 112, 112), null),

        // Lower visible purple rune area. These slots use wider spacing than the blue rune groups.
        new(new Rectangle(68, 1170, 112, 112), null),
        new(new Rectangle(203, 1170, 112, 112), null),
        new(new Rectangle(338, 1170, 112, 112), null),
        new(new Rectangle(473, 1170, 112, 112), null),
        new(new Rectangle(878, 1170, 112, 112), null),
        new(new Rectangle(1013, 1170, 112, 112), null),
        new(new Rectangle(1148, 1170, 112, 112), null),

        new(new Rectangle(68, 1305, 112, 112), null),
        new(new Rectangle(203, 1305, 112, 112), null),
        new(new Rectangle(338, 1305, 112, 112), null),
        new(new Rectangle(473, 1305, 112, 112), null),
        new(new Rectangle(608, 1305, 112, 112), null),
        new(new Rectangle(743, 1305, 112, 112), null),
        new(new Rectangle(878, 1305, 112, 112), null),
        new(new Rectangle(1013, 1305, 112, 112), null),
        new(new Rectangle(1148, 1305, 112, 112), null),

        new(new Rectangle(68, 1440, 112, 112), null),
        new(new Rectangle(203, 1440, 112, 112), null),
        new(new Rectangle(338, 1440, 112, 112), null),
        new(new Rectangle(473, 1440, 112, 112), null),
        new(new Rectangle(608, 1440, 112, 112), null),
        new(new Rectangle(743, 1440, 112, 112), null),
        new(new Rectangle(878, 1440, 112, 112), null),
        new(new Rectangle(1013, 1440, 112, 112), null),
        new(new Rectangle(1148, 1440, 112, 112), null),
    ];
}
