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
        if (_cachedPrices is null || DateTimeOffset.UtcNow - _lastPriceRefresh > TimeSpan.FromMinutes(30))
        {
            await RefreshPricesAsync(cancellationToken).ConfigureAwait(false);
        }

        CurrencyScanner.SaveBitmap(screenshot, Path.Combine(_debugDirectory, "kalguuran-runes-fullscreen.png"));
        var stashCropPath = Path.Combine(_debugDirectory, "kalguuran-runes-stash-crop.png");
        using var stashCrop = screenshot.Clone(layout.DisplayCropRegion, screenshot.PixelFormat);
        CurrencyScanner.SaveBitmap(stashCrop, stashCropPath);

        var tessData = await CurrencyScanner.EnsureTessDataAsync(Path.Combine(AppContext.BaseDirectory, "tessdata"), cancellationToken).ConfigureAwait(false);
        var stacks = new List<RuneStack>();
        var detections = new List<RuneSlotDetection>();
        var countDebugLines = new List<string>();
        var knownOccupied = 0;
        var unknownOccupied = 0;

        for (var slotIndex = 0; slotIndex < KalguuranRuneSlotMap.Slots.Length; slotIndex++)
        {
            var slot = KalguuranRuneSlotMap.Slots[slotIndex];
            var scanSlot = slot with { Bounds = OffsetRectangle(slot.Bounds, layout.SlotOffset) };
            var itemName = _mappingStore.GetName(slotIndex, slot.ItemName);
            var countOverride = _mappingStore.GetCountOverride(slotIndex);
            if (countOverride == 0)
            {
                countDebugLines.Add($"Slot {slotIndex,2} {itemName ?? "(mapped slot)"}: forced empty override x0");
                detections.Add(BuildDetection(slotIndex, scanSlot, layout, false, itemName, 0, null, null));
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

                detections.Add(BuildDetection(slotIndex, scanSlot, layout, false, itemName, 0, null, null));
                continue;
            }

            if (itemName is null)
            {
                unknownOccupied++;
                var unknownQuantityRead = StackCountReader.ReadRuneQuantity(
                    screenshot,
                    scanSlot.Bounds,
                    tessData,
                    new StackCountReadOptions(_debugDirectory, "kalguuran-runes", slotIndex));
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
                detections.Add(BuildDetection(slotIndex, scanSlot, layout, true, null, unknownQuantity, null, null, unknownQuantityRead));
                continue;
            }

            knownOccupied++;
            var quantityRead = StackCountReader.ReadRuneQuantity(
                screenshot,
                scanSlot.Bounds,
                tessData,
                new StackCountReadOptions(_debugDirectory, "kalguuran-runes", slotIndex));
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
                detections.Add(BuildDetection(slotIndex, scanSlot, layout, true, itemName, quantity, null, null, quantityRead));
                continue;
            }

            stacks.Add(new RuneStack(itemName, quantity, value.Exalts, value.Divines));
            detections.Add(BuildDetection(slotIndex, scanSlot, layout, true, itemName, quantity, value.Exalts, value.Divines, quantityRead));
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
        var overlayBounds = FixedStashSlot.Inset(slot.Bounds, FixedStashScannerProfiles.DefaultStaticOverlayInset);
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

    private static Rectangle OffsetRectangle(Rectangle rectangle, Point offset)
    {
        return new Rectangle(
            rectangle.X + offset.X,
            rectangle.Y + offset.Y,
            rectangle.Width,
            rectangle.Height);
    }

}

internal static class KalguuranRuneSlotMap
{
    private const int FolderCropX = 25;
    private const int FolderCropY = 400;

    // First pass from AI-assisted Kalguuran rune capture. Stored in folder crop-space plus the crop origin.
    public static readonly RuneSlot[] Slots =
    [
        Slot(244, 64, 118, 121),
        Slot(377, 64, 118, 121),
        Slot(510, 64, 118, 121),
        Slot(642, 64, 118, 121),
        Slot(775, 64, 118, 121),
        Slot(908, 64, 118, 121),

        Slot(244, 203, 118, 121),
        Slot(377, 203, 118, 121),
        Slot(510, 203, 118, 121),
        Slot(642, 203, 118, 121),
        Slot(775, 203, 118, 121),
        Slot(908, 203, 118, 121),

        Slot(312, 341, 118, 121),
        Slot(445, 341, 118, 121),
        Slot(578, 341, 118, 121),
        Slot(711, 341, 118, 121),
        Slot(843, 341, 118, 121),

        Slot(58, 489, 118, 121),
        Slot(191, 489, 118, 121),
        Slot(324, 489, 118, 121),
        Slot(457, 489, 118, 121),
        Slot(590, 489, 118, 121),
        Slot(723, 489, 118, 121),
        Slot(856, 489, 118, 121),
        Slot(989, 489, 118, 121),
        Slot(1122, 489, 118, 121),

        Slot(58, 624, 118, 121),
        Slot(191, 624, 118, 121),
        Slot(324, 624, 118, 121),
        Slot(457, 624, 118, 121),
        Slot(723, 624, 118, 121),
        Slot(856, 624, 118, 121),
        Slot(989, 624, 118, 121),
        Slot(1122, 624, 118, 121),

        Slot(58, 758, 118, 121),
        Slot(191, 758, 118, 121),
        Slot(324, 758, 118, 121),
        Slot(457, 758, 118, 121),
        Slot(590, 758, 118, 121),
        Slot(723, 758, 118, 121),
        Slot(856, 758, 118, 121),
        Slot(989, 758, 118, 121),
        Slot(1122, 758, 118, 121),

        // Slot 43 was originally a false positive in the blank left gutter above the bottom row.
        // Keep the index stable by moving it to the real bottom-left cell.
        Slot(58, 1042, 118, 121),
        Slot(191, 906, 118, 121),
        Slot(324, 906, 118, 121),
        Slot(457, 906, 118, 121),
        Slot(590, 906, 118, 121),
        Slot(723, 906, 118, 121),
        Slot(856, 906, 118, 121),
        Slot(989, 906, 118, 121),
        // Slot 51 was originally a false positive in the blank right gutter above the bottom row.
        // Keep the index stable by moving it to the real bottom-right cell.
        Slot(1122, 1042, 118, 121),

        // Appended to preserve existing user mapping indexes for earlier slots.
        Slot(191, 1042, 118, 121),
        Slot(324, 1042, 118, 121),
        Slot(457, 1042, 118, 121),
        Slot(590, 1042, 118, 121),
        Slot(723, 1042, 118, 121),
        Slot(856, 1042, 118, 121),
    ];

    private static RuneSlot Slot(int cropX, int cropY, int width, int height)
    {
        return new RuneSlot(
            new Rectangle(FolderCropX + cropX, FolderCropY + cropY, width, height),
            null);
    }
}
