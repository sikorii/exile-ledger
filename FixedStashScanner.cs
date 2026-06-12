namespace Poe2PriceChecker;

internal sealed class FixedStashScanner
{
    private readonly string _debugDirectory;
    private readonly CurrencyMappingStore _mappingStore;
    private readonly FixedStashScannerProfile _profile;
    private PoeNinjaPrices? _cachedPrices;
    private DateTimeOffset _lastPriceRefresh = DateTimeOffset.MinValue;

    public FixedStashScanner(string debugDirectory, CurrencyMappingStore mappingStore, FixedStashScannerProfile profile)
    {
        _debugDirectory = debugDirectory;
        _mappingStore = mappingStore;
        _profile = profile;
        Directory.CreateDirectory(_debugDirectory);
    }

    public int SetSlot(int slotIndex, string itemName, int? countOverride)
    {
        var mappedSlots = 0;
        if (EssenceStaticIdentity.IsEssenceProfile(_profile))
        {
            mappedSlots = EssenceStaticIdentity.ApplyGroupMapping(_profile.Slots, _mappingStore, slotIndex, itemName);
        }

        if (mappedSlots == 0)
        {
            _mappingStore.SetName(slotIndex, itemName);
            mappedSlots = string.IsNullOrWhiteSpace(itemName) ? 0 : 1;
        }

        _mappingStore.SetCountOverride(slotIndex, countOverride);
        return mappedSlots;
    }

    public int? GetCountOverride(int slotIndex)
    {
        return _mappingStore.GetCountOverride(slotIndex);
    }

    public async Task RefreshPricesAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        _cachedPrices = await PoeNinjaPrices.FetchAsync(cancellationToken, forceRefresh).ConfigureAwait(false);
        _lastPriceRefresh = DateTimeOffset.UtcNow;
    }

    public async Task<FixedStashScanResult> ScanScreenAsync(CancellationToken cancellationToken, StashLayoutProfile layout)
    {
        var screen = ScreenCaptureService.SelectPoeScreen();
        using var screenshot = ScreenCaptureService.CaptureScreen(screen.Bounds);
        return await ScanBitmapAsync(screenshot, screen.Bounds, cancellationToken, layout).ConfigureAwait(false);
    }

    public async Task<FixedStashScanResult> ScanFileAsync(string screenshotPath, CancellationToken cancellationToken, StashLayoutProfile layout)
    {
        using var screenshot = CurrencyScanner.LoadBitmapWithoutFileLock(screenshotPath);
        return await ScanBitmapAsync(screenshot, new Rectangle(0, 0, screenshot.Width, screenshot.Height), cancellationToken, layout).ConfigureAwait(false);
    }

    private async Task<FixedStashScanResult> ScanBitmapAsync(
        Bitmap screenshot,
        Rectangle screenBounds,
        CancellationToken cancellationToken,
        StashLayoutProfile layout)
    {
        if (_cachedPrices is null || DateTimeOffset.UtcNow - _lastPriceRefresh > TimeSpan.FromMinutes(30))
        {
            await RefreshPricesAsync(cancellationToken).ConfigureAwait(false);
        }

        var safeKey = _profile.CountMode;
        CurrencyScanner.SaveBitmap(screenshot, Path.Combine(_debugDirectory, $"{safeKey}-fullscreen.png"));
        var stashCropPath = Path.Combine(_debugDirectory, $"{safeKey}-stash-crop.png");
        using var stashCrop = screenshot.Clone(layout.DisplayCropRegion, screenshot.PixelFormat);
        CurrencyScanner.SaveBitmap(stashCrop, stashCropPath);

        var tessData = await CurrencyScanner.EnsureTessDataAsync(Path.Combine(AppContext.BaseDirectory, "tessdata"), cancellationToken).ConfigureAwait(false);
        var stacks = new List<FixedStashStack>();
        var detections = new List<FixedStashSlotDetection>();
        var countDebugLines = new List<string>();
        var knownOccupied = 0;
        var unknownOccupied = 0;
        var resolvedEssenceNames = EssenceStaticIdentity.IsEssenceProfile(_profile)
            ? EssenceStaticIdentity.ResolveSlotNames(_profile.Slots, _mappingStore)
            : null;

        for (var slotIndex = 0; slotIndex < _profile.Slots.Count; slotIndex++)
        {
            var slot = _profile.Slots[slotIndex];
            var scanSlot = OffsetSlot(slot, layout.SlotOffset);
            if (!ContainsRectangle(screenshot.Size, scanSlot.Bounds))
            {
                countDebugLines.Add($"Slot {slotIndex,2}: outside screenshot; skipped");
                continue;
            }

            var itemName = resolvedEssenceNames?[slotIndex] ?? _mappingStore.GetName(slotIndex, slot.ItemName);
            var countOverride = _mappingStore.GetCountOverride(slotIndex);
            if (countOverride == 0)
            {
                countDebugLines.Add($"Slot {slotIndex,2} {itemName ?? "(mapped slot)"}: forced empty override x0");
                detections.Add(BuildDetection(slotIndex, scanSlot, layout, false, itemName, 0, null, null));
                continue;
            }

            var occupied = IsOccupied(screenshot, scanSlot.Bounds, itemName, countOverride);
            if (!occupied)
            {
                if (itemName is not null || countOverride is not null)
                {
                    countDebugLines.Add(
                        $"Slot {slotIndex,2} {itemName ?? "(mapped slot)"}: empty visual slot, skipped" +
                        (countOverride is null ? string.Empty : $" override x{countOverride} ignored"));
                }

                detections.Add(BuildDetection(slotIndex, scanSlot, layout, false, itemName, 0, null, null));
                continue;
            }

            var quantityRead = ReadQuantity(screenshot, scanSlot.Bounds, tessData, slotIndex);
            var quantity = countOverride ?? quantityRead.Quantity;
            var trainingStatus = CountTrainingHelpers.TrySaveFromOverride(
                screenshot,
                scanSlot.Bounds,
                countOverride,
                _profile.CountMode,
                slotIndex,
                _debugDirectory);

            countDebugLines.Add(
                $"Slot {slotIndex,2} {itemName ?? "(unknown)"}: read x{quantityRead.Quantity}" +
                (countOverride is null ? string.Empty : $" override x{countOverride}") +
                (trainingStatus.Length == 0 ? string.Empty : $" ({trainingStatus})") +
                $" ({quantityRead.DebugText})");

            if (itemName is null)
            {
                unknownOccupied++;
                if (resolvedEssenceNames is not null)
                {
                    countDebugLines.Add($"  Slot {slotIndex,2}: incomplete Essence static identity; no family inferred for this group.");
                }

                detections.Add(BuildDetection(slotIndex, scanSlot, layout, true, null, quantity, null, null, quantityRead));
                continue;
            }

            knownOccupied++;
            var value = _cachedPrices!.TryGetValue(itemName, quantity);
            if (value is null)
            {
                unknownOccupied++;
                countDebugLines.Add("  " + _cachedPrices.DiagnoseMissing(itemName, _profile.PriceCategories).ToDebugString());
                detections.Add(BuildDetection(slotIndex, scanSlot, layout, true, itemName, quantity, null, null, quantityRead));
                continue;
            }

            stacks.Add(new FixedStashStack(itemName, quantity, value.Exalts, value.Divines));
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
            Path.Combine(_debugDirectory, $"{safeKey}-debug.txt"),
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

        return new FixedStashScanResult(
            _profile,
            topStacks,
            totalExalts,
            totalDivines,
            knownOccupied,
            unknownOccupied,
            screenBounds,
            stashCropPath,
            detections);
    }

    private bool IsOccupied(Bitmap screenshot, Rectangle slotBounds, string? itemName, int? countOverride)
    {
        if (countOverride is not null)
        {
            return true;
        }

        if (_profile.IsRuneLike)
        {
            return StashSlotVisuals.HasVisibleQuantityMarker(screenshot, slotBounds) ||
                (itemName is not null && StashSlotVisuals.HasRuneBodySignal(screenshot, slotBounds)) ||
                StashSlotVisuals.HasGenericItemSignal(screenshot, slotBounds);
        }

        var definitelyBlank = CurrencyScanner.IsDefinitelyBlank(screenshot, slotBounds);
        return !definitelyBlank && (CurrencyScanner.IsOccupied(screenshot, slotBounds) || itemName is not null);
    }

    private QuantityReadResult ReadQuantity(Bitmap screenshot, Rectangle slotBounds, string tessData, int slotIndex)
    {
        var options = new StackCountReadOptions(_debugDirectory, _profile.CountMode, slotIndex);
        return _profile.IsRuneLike
            ? StackCountReader.ReadRuneQuantity(screenshot, slotBounds, tessData, options)
            : StackCountReader.ReadQuantity(screenshot, slotBounds, tessData, options);
    }

    private FixedStashSlotDetection BuildDetection(
        int slotIndex,
        FixedStashSlot slot,
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
        var overlayBounds = slot.GetOverlayBounds();
        var overlayCropBounds = new Rectangle(
            overlayBounds.X - layout.DisplayCropRegion.X,
            overlayBounds.Y - layout.DisplayCropRegion.Y,
            overlayBounds.Width,
            overlayBounds.Height);

        return new FixedStashSlotDetection(
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

    private static FixedStashSlot OffsetSlot(FixedStashSlot slot, Point offset)
    {
        return slot with
        {
            Bounds = OffsetRectangle(slot.Bounds, offset),
            OverlayBounds = slot.OverlayBounds is null
                ? null
                : OffsetRectangle(slot.OverlayBounds.Value, offset)
        };
    }

    private static Rectangle OffsetRectangle(Rectangle rectangle, Point offset)
    {
        return new Rectangle(
            rectangle.X + offset.X,
            rectangle.Y + offset.Y,
            rectangle.Width,
            rectangle.Height);
    }

    private static bool ContainsRectangle(Size size, Rectangle rectangle)
    {
        return rectangle.X >= 0 &&
            rectangle.Y >= 0 &&
            rectangle.Right <= size.Width &&
            rectangle.Bottom <= size.Height;
    }
}
