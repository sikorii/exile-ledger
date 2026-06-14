namespace Poe2PriceChecker;

internal sealed class KalguuranRuneScanner
{
    private readonly string _debugDirectory;
    private readonly CurrencyMappingStore _mappingStore;
    private PoeNinjaPrices? _cachedPrices;
    private DateTimeOffset _lastPriceRefresh = DateTimeOffset.MinValue;

    public KalguuranRuneScanner(string debugDirectory, CurrencyMappingStore mappingStore)
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
            static (_, _) => Array.Empty<RuneUpgradeSuggestion>());
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

        CurrencyScanner.SaveBitmap(screenshot, Path.Combine(_debugDirectory, "kalguuran-runes-fullscreen.png"));
        var stashCropPath = Path.Combine(_debugDirectory, "kalguuran-runes-stash-crop.png");
        using var stashCrop = screenshot.Clone(actualLayout.DisplayCropRegion, screenshot.PixelFormat);
        CurrencyScanner.SaveBitmap(stashCrop, stashCropPath);

        var tessData = await CurrencyScanner.EnsureTessDataAsync(Path.Combine(AppContext.BaseDirectory, "tessdata"), cancellationToken).ConfigureAwait(false);
        var scanId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var stacks = new List<RuneStack>();
        var detections = new List<RuneSlotDetection>();
        var countDebugLines = new List<string>();
        var knownOccupied = 0;
        var unknownOccupied = 0;

        for (var slotIndex = 0; slotIndex < KalguuranRuneSlotMap.Slots.Length; slotIndex++)
        {
            var slot = KalguuranRuneSlotMap.Slots[slotIndex];
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
                    new StackCountReadOptions(_debugDirectory, "kalguuran-runes", slotIndex, scanId, mapper.Profile.ScaleX));
                var unknownQuantity = countOverride ?? unknownQuantityRead.Quantity;
                var unknownTrainingStatus = CountTrainingHelpers.TrySaveFromOverride(
                    screenshot,
                    scanSlot.Bounds,
                    countOverride,
                    "kalguuran-runes",
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
                new StackCountReadOptions(_debugDirectory, "kalguuran-runes", slotIndex, scanId, mapper.Profile.ScaleX));
            var quantity = countOverride ?? quantityRead.Quantity;
            var knownTrainingStatus = CountTrainingHelpers.TrySaveFromOverride(
                screenshot,
                scanSlot.Bounds,
                countOverride,
                "kalguuran-runes",
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
                countDebugLines.Add("  " + _cachedPrices.DiagnoseMissing(itemName, FixedStashScannerProfiles.KalguuranRunes.PriceCategories).ToDebugString());
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
        var priceSummary = _cachedPrices!.CacheSummary;

        File.WriteAllLines(
            Path.Combine(_debugDirectory, "kalguuran-runes-debug.txt"),
            stacks.OrderByDescending(stack => stack.Exalts)
                .Select(stack => $"{stack.ItemName} x{stack.Quantity} = {stack.Exalts:0.##} ex / {stack.Divines:0.####} div")
                .Concat([
                    $"Price cache: {priceSummary.ItemCount} items fetched {priceSummary.FetchedUtc:O}",
                    $"Categories: {string.Join(", ", priceSummary.FetchedCategories)}",
                    $"Known occupied: {knownOccupied}",
                    $"Unknown occupied: {unknownOccupied}",
                    string.Empty,
                    "Count reads:"
                ])
                .Concat(countDebugLines));

        return new RuneScanResult(
            topStacks,
            [],
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
        var overlayBounds = FixedStashSlot.Inset(slot.Bounds, FixedStashScannerProfiles.KalguuranRuneOverlayInset);
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

}

internal static class KalguuranRuneSlotMap
{
    private const int FolderCropX = 25;
    private const int FolderFullCropY = 330;

    // Stored in FolderFull crop-space plus the crop origin so final overlays snap to the visible rune frames.
    public static readonly RuneSlot[] Slots =
    [
        Slot(245, 137), Slot(380, 137), Slot(515, 137), Slot(650, 137), Slot(785, 137), Slot(920, 137),
        Slot(245, 271), Slot(380, 271), Slot(515, 271), Slot(650, 271), Slot(785, 271), Slot(920, 271),
        Slot(310, 404, 125, 126), Slot(445, 404, 125, 126), Slot(580, 404, 125, 126), Slot(715, 404, 125, 126), Slot(850, 404, 125, 126),
        Slot(42, 554, 121, 121), Slot(177, 554, 121, 121), Slot(312, 554, 121, 121), Slot(447, 554, 121, 121), Slot(582, 554, 121, 121), Slot(717, 554, 121, 121), Slot(852, 554, 121, 121), Slot(987, 554, 121, 121), Slot(1122, 554, 121, 121),
        Slot(42, 689, 121, 124), Slot(177, 689, 121, 123), Slot(312, 689, 121, 121), Slot(447, 689, 121, 121), Slot(717, 689, 121, 121), Slot(852, 689, 121, 121), Slot(987, 689, 121, 121), Slot(1122, 689, 123, 121),
        Slot(42, 822, 121, 123), Slot(177, 824, 121, 123), Slot(312, 824, 121, 121), Slot(447, 824, 121, 121), Slot(582, 824, 121, 121), Slot(717, 824, 121, 121), Slot(852, 824, 121, 121), Slot(987, 824, 123, 121), Slot(1119, 824, 124, 121),
        Slot(174, 969, 126, 128), Slot(310, 971, 125, 126), Slot(445, 971, 125, 126), Slot(580, 971, 125, 126), Slot(715, 971, 125, 126), Slot(850, 971, 125, 126), Slot(985, 971, 125, 126),
        Slot(40, 1106, 125, 128), Slot(175, 1105, 125, 127), Slot(310, 1106, 125, 126), Slot(445, 1106, 125, 128), Slot(580, 1106, 125, 126), Slot(715, 1106, 125, 126), Slot(850, 1106, 125, 126), Slot(1120, 1106, 126, 126),
    ];

    private static RuneSlot Slot(int cropX, int folderFullCropY, int width = 120, int height = 120)
    {
        return new RuneSlot(
            new Rectangle(FolderCropX + cropX, FolderFullCropY + folderFullCropY, width, height),
            null);
    }
}
