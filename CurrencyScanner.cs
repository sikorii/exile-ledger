namespace Poe2PriceChecker;

internal sealed class CurrencyScanner
{
    internal static readonly Rectangle StashCropRegion = StashLayoutProfile.Normal.DisplayCropRegion;

    private readonly string _debugDirectory;
    private readonly CurrencyMappingStore _mappingStore;
    private LiveMarketPrices? _cachedPrices;
    private DateTimeOffset _lastPriceRefresh = DateTimeOffset.MinValue;

    public CurrencyScanner(string debugDirectory, CurrencyMappingStore mappingStore)
    {
        _debugDirectory = debugDirectory;
        _mappingStore = mappingStore;
        Directory.CreateDirectory(_debugDirectory);
    }

    public void SetSlotName(int slotIndex, string itemName)
    {
        _mappingStore.SetName(slotIndex, itemName);
    }

    public void SetSlot(int slotIndex, string itemName, int? countOverride)
    {
        _mappingStore.SetName(slotIndex, itemName);
        _mappingStore.SetCountOverride(slotIndex, countOverride);
    }

    public async Task RefreshPricesAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        _cachedPrices = await LiveMarketPrices.FetchAsync(cancellationToken, forceRefresh).ConfigureAwait(false);
        _lastPriceRefresh = DateTimeOffset.UtcNow;
    }

    public async Task<CurrencyScanResult> RecalculateValuesAsync(CurrencyScanResult result, CancellationToken cancellationToken)
    {
        await EnsurePricesAsync(cancellationToken).ConfigureAwait(false);
        return ScanValueRecalculator.Recalculate(result, _cachedPrices!);
    }

    public async Task<CurrencyScanResult> ScanScreenAsync(CancellationToken cancellationToken, StashLayoutProfile? layout = null)
    {
        var screen = ScreenCaptureService.SelectPoeScreen();
        using var screenshot = ScreenCaptureService.CaptureScreen(screen.Bounds);
        return await ScanBitmapAsync(screenshot, screen.Bounds, cancellationToken, layout ?? StashLayoutProfile.Normal).ConfigureAwait(false);
    }

    public async Task<CurrencyScanResult> ScanFileAsync(string screenshotPath, CancellationToken cancellationToken, StashLayoutProfile? layout = null)
    {
        using var screenshot = LoadBitmapWithoutFileLock(screenshotPath);
        return await ScanBitmapAsync(screenshot, new Rectangle(0, 0, screenshot.Width, screenshot.Height), cancellationToken, layout ?? StashLayoutProfile.Normal).ConfigureAwait(false);
    }

    private async Task<CurrencyScanResult> ScanBitmapAsync(Bitmap screenshot, Rectangle screenBounds, CancellationToken cancellationToken, StashLayoutProfile layout)
    {
        await EnsurePricesAsync(cancellationToken).ConfigureAwait(false);
        var mapper = StashCoordinateMapper.FromScreenshotSize(screenshot.Size);
        var actualLayout = mapper.ScaleLayoutFromBase(layout);
        var resolutionDebugLines = mapper.BuildDebugLines(screenshot.Size, screenBounds, layout, actualLayout);

        SaveBitmap(screenshot, Path.Combine(_debugDirectory, "currency-fullscreen.png"));
        var stashCropPath = Path.Combine(_debugDirectory, "currency-stash-crop.png");
        using var stashCrop = screenshot.Clone(actualLayout.DisplayCropRegion, screenshot.PixelFormat);
        SaveBitmap(stashCrop, stashCropPath);

        var tessData = await EnsureTessDataAsync(Path.Combine(AppContext.BaseDirectory, "tessdata"), cancellationToken).ConfigureAwait(false);
        var scanId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var stacks = new List<CurrencyStack>();
        var detections = new List<CurrencySlotDetection>();
        var countDebugLines = new List<string>();
        var knownOccupied = 0;
        var unknownOccupied = 0;

        for (var slotIndex = 0; slotIndex < CurrencySlotMap.Slots.Length; slotIndex++)
        {
            var slot = CurrencySlotMap.Slots[slotIndex];
            var scanSlot = slot with { Bounds = mapper.OffsetAndScaleRectFromBase(slot.Bounds, layout.SlotOffset) };
            var itemName = _mappingStore.GetName(slotIndex, slot.ItemName);
            var countOverride = _mappingStore.GetCountOverride(slotIndex);
            if (countOverride == 0)
            {
                countDebugLines.Add($"Slot {slotIndex,2} {itemName ?? "(mapped slot)"}: forced empty override x0");
                detections.Add(BuildDetection(slotIndex, scanSlot, actualLayout, false, itemName, 0, null, null));
                continue;
            }

            var definitelyBlank = IsDefinitelyBlank(screenshot, scanSlot.Bounds);
            var occupied = !definitelyBlank && (IsOccupied(screenshot, scanSlot.Bounds) || itemName is not null || countOverride is not null);
            if (!occupied)
            {
                if (itemName is not null || countOverride is not null)
                {
                    countDebugLines.Add(
                        $"Slot {slotIndex,2} {itemName ?? "(mapped slot)"}: empty visual slot, skipped" +
                        (countOverride is null ? string.Empty : $" override x{countOverride} ignored"));
                }

                detections.Add(BuildDetection(slotIndex, scanSlot, actualLayout, false, itemName, 0, null, null));
                continue;
            }

            if (itemName is null)
            {
                unknownOccupied++;
                var unknownQuantityRead = StackCountReader.ReadQuantity(
                    screenshot,
                    scanSlot.Bounds,
                    tessData,
                    new StackCountReadOptions(_debugDirectory, "currency", slotIndex, scanId, mapper.Profile.ScaleX));
                var unknownQuantity = countOverride ?? unknownQuantityRead.Quantity;
                var unknownTrainingStatus = CountTrainingHelpers.TrySaveFromOverride(
                    screenshot,
                    scanSlot.Bounds,
                    countOverride,
                    "currency",
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
            var quantityRead = StackCountReader.ReadQuantity(
                screenshot,
                scanSlot.Bounds,
                tessData,
                new StackCountReadOptions(_debugDirectory, "currency", slotIndex, scanId, mapper.Profile.ScaleX));
            var quantity = countOverride ?? quantityRead.Quantity;
            var knownTrainingStatus = CountTrainingHelpers.TrySaveFromOverride(
                screenshot,
                scanSlot.Bounds,
                countOverride,
                "currency",
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
                countDebugLines.Add("  " + _cachedPrices.DiagnoseMissing(itemName, FixedStashScannerProfiles.Currency.PriceCategories).ToDebugString());
                detections.Add(BuildDetection(slotIndex, scanSlot, actualLayout, true, itemName, quantity, null, null, quantityRead));
                continue;
            }

            stacks.Add(new CurrencyStack(itemName, quantity, value.Exalts, value.Divines));
            countDebugLines.Add("  " + _cachedPrices.FormatSourceDebug(itemName, quantity, value));
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
            Path.Combine(_debugDirectory, "currency-debug.txt"),
            resolutionDebugLines
                .Concat(stacks.OrderByDescending(stack => stack.Exalts)
                .Select(stack => $"{stack.ItemName} x{stack.Quantity} = {stack.Exalts:0.##} ex / {stack.Divines:0.####} div")
                .Concat([
                    $"Price cache: {priceSummary.ItemCount} items fetched {priceSummary.FetchedUtc:O}",
                    $"Categories: {string.Join(", ", priceSummary.FetchedCategories)}",
                    $"Known occupied: {knownOccupied}",
                    $"Unknown occupied: {unknownOccupied}",
                    string.Empty,
                    "Count reads:"
                ])
                .Concat(countDebugLines)));

        return new CurrencyScanResult(
            topStacks,
            totalExalts,
            totalDivines,
            knownOccupied,
            unknownOccupied,
            screenBounds,
            stashCropPath,
            detections);
    }

    private CurrencySlotDetection BuildDetection(
        int slotIndex,
        CurrencySlot slot,
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

        return new CurrencySlotDetection(
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

    internal static QuantityReadResult ReadQuantity(Bitmap screenshot, Rectangle slotBounds, string tessData)
    {
        var coordinateScale = ScreenshotResolutionProfile.DetectScaleOrDefault(screenshot.Size);
        return StackCountReader.ReadQuantity(
            screenshot,
            slotBounds,
            tessData,
            StackCountReadOptions.Default with { CoordinateScale = coordinateScale });
    }

    internal static bool IsOccupied(Bitmap screenshot, Rectangle slotBounds)
    {
        var coordinateScale = ScreenshotResolutionProfile.DetectScaleOrDefault(screenshot.Size);
        var inset = ScaleLength(12, coordinateScale);
        using var crop = screenshot.Clone(new Rectangle(slotBounds.X + inset, slotBounds.Y + inset, slotBounds.Width - inset * 2, slotBounds.Height - inset * 2), screenshot.PixelFormat);
        var colorfulPixels = 0;
        var sampled = 0;
        for (var y = 0; y < crop.Height; y += 4)
        {
            for (var x = 0; x < crop.Width; x += 4)
            {
                var c = crop.GetPixel(x, y);
                var max = Math.Max(c.R, Math.Max(c.G, c.B));
                var min = Math.Min(c.R, Math.Min(c.G, c.B));
                if (max > 55 && max - min > 22)
                {
                    colorfulPixels++;
                }

                sampled++;
            }
        }

        return sampled > 0 && colorfulPixels / (double)sampled > 0.12;
    }

    internal static bool IsDefinitelyBlank(Bitmap screenshot, Rectangle slotBounds)
    {
        var coordinateScale = ScreenshotResolutionProfile.DetectScaleOrDefault(screenshot.Size);
        var inset = ScaleLength(12, coordinateScale);
        using var crop = screenshot.Clone(new Rectangle(slotBounds.X + inset, slotBounds.Y + inset, slotBounds.Width - inset * 2, slotBounds.Height - inset * 2), screenshot.PixelFormat);
        var brightPixels = 0;
        var midBrightPixels = 0;
        var sampled = 0;
        double luminanceTotal = 0;
        double luminanceSquaredTotal = 0;

        for (var y = 0; y < crop.Height; y += 4)
        {
            for (var x = 0; x < crop.Width; x += 4)
            {
                var c = crop.GetPixel(x, y);
                var max = Math.Max(c.R, Math.Max(c.G, c.B));
                var luminance = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
                if (max > 80)
                {
                    brightPixels++;
                }

                if (max > 45)
                {
                    midBrightPixels++;
                }

                luminanceTotal += luminance;
                luminanceSquaredTotal += luminance * luminance;
                sampled++;
            }
        }

        if (sampled == 0)
        {
            return true;
        }

        var average = luminanceTotal / sampled;
        var variance = Math.Max(0, luminanceSquaredTotal / sampled - average * average);
        var standardDeviation = Math.Sqrt(variance);
        return average < 24 &&
            standardDeviation < 22 &&
            brightPixels / (double)sampled < 0.04 &&
            midBrightPixels / (double)sampled < 0.10;
    }

    internal static void SaveBitmap(Bitmap bitmap, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(
            directory ?? AppContext.BaseDirectory,
            $"{Path.GetFileNameWithoutExtension(path)}-{Guid.NewGuid():N}.tmp.png");

        bitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
        File.Move(tempPath, path, overwrite: true);
    }

    internal static Bitmap LoadBitmapWithoutFileLock(string path)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        using var loaded = Image.FromStream(stream);
        return new Bitmap(loaded);
    }

    private static int ScaleLength(int baseValue, double scale)
    {
        return Math.Max(1, (int)Math.Round(baseValue * scale, MidpointRounding.AwayFromZero));
    }

    internal static async Task<string> EnsureTessDataAsync(string tessDataDirectory, CancellationToken cancellationToken)
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

}

internal sealed record CurrencySlot(Rectangle Bounds, string? ItemName);

internal static class CurrencySlotMap
{
    private const int NormalCropX = 25;
    private const int NormalCropY = 250;
    private const int SlotSize = 120;

    // 3840x2160 currency tab map, stored in normal crop-space coordinates plus the crop origin.
    public static readonly CurrencySlot[] Slots =
    [
        Slot(40, 55, "Orb of Transmutation"),
        Slot(160, 55, null),
        Slot(280, 55, null),
        Slot(445, 55, null),
        Slot(580, 55, null),
        Slot(715, 55, null),
        Slot(875, 55, null),
        Slot(1000, 55, null),

        Slot(40, 190, null),
        Slot(160, 190, null),
        Slot(280, 190, null),
        Slot(445, 190, null),
        Slot(580, 190, null),
        Slot(715, 190, null),
        Slot(1120, 190, null),

        Slot(40, 325, "Regal Orb"),
        Slot(160, 325, null),
        Slot(280, 325, null),
        Slot(510, 325, null),
        Slot(650, 325, null),
        Slot(850, 360, null),
        Slot(985, 360, null),
        Slot(1120, 360, null),

        Slot(40, 460, "Exalted Orb"),
        Slot(160, 460, null),
        Slot(280, 460, null),
        Slot(985, 495, null),
        Slot(1120, 495, null),

        Slot(40, 595, null),
        Slot(160, 595, null),
        Slot(280, 595, null),
        Slot(1120, 680, null),

        Slot(380, 850, null),
        Slot(515, 850, null),
        Slot(650, 850, null),
        Slot(785, 850, null),

        Slot(215, 1010, null),
        Slot(335, 1010, null),
        Slot(455, 1010, null),
        Slot(575, 1010, null),
        Slot(695, 1010, null),
        Slot(815, 1010, null),
        Slot(935, 1010, null),

        Slot(215, 1130, null),
        Slot(335, 1130, null),
        Slot(455, 1130, null),
        Slot(575, 1130, null),
        Slot(695, 1130, null),
        Slot(815, 1130, null),
        Slot(935, 1130, null),

        // Appended outside visual order for a top-right special currency slot.
        Slot(1120, 55, "Perfect Jeweller's Orb"),
    ];

    private static CurrencySlot Slot(int cropX, int cropY, string? itemName)
    {
        return new CurrencySlot(
            new Rectangle(NormalCropX + cropX, NormalCropY + cropY, SlotSize, SlotSize),
            itemName);
    }
}
