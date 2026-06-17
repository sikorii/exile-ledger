namespace Poe2PriceChecker;

internal sealed record RewardChoice(
    int Quantity,
    string ItemName,
    decimal Exalts,
    decimal Divines,
    ChoiceColor Color)
{
    public string DisplayText => $"{Quantity}x {ItemName}   {Exalts:0.##} ex / {Divines:0.####} div";

    public string CompactPriceText => Divines >= 1m
        ? $"{Divines:0.##} div / {Exalts:0.##} ex"
        : $"{Exalts:0.##} ex";

    public string CompactPriceBasis => Divines >= 1m
        ? "div>=1 show div+ex"
        : "div<1 show ex";
}

internal enum ChoiceColor
{
    Red,
    Yellow,
    Green
}

internal sealed record ScanResult(
    IReadOnlyList<RewardChoice> Choices,
    IReadOnlyList<string> UnpricedRewards,
    IReadOnlyList<string> Notes,
    string RawOcrText,
    Rectangle CaptureRegion,
    Rectangle ScreenBounds,
    IReadOnlyList<RuneshapingOverlayLabel> OverlayLabels);

internal sealed record RuneshapingOverlayLabel(
    string Text,
    ChoiceColor Color,
    Rectangle LabelBounds,
    Rectangle? RowBounds,
    string PlacementMode,
    string ValueBasis);

internal sealed record RawReward(int Quantity, string ItemName);

internal sealed record CurrencyStack(string ItemName, int Quantity, decimal Exalts, decimal Divines)
{
    public string DisplayText => $"{ItemName,-34} x{Quantity,4} {Exalts,10:0.##} ex / {Divines,8:0.####} div";
}

internal sealed record CurrencyScanResult(
    IReadOnlyList<CurrencyStack> TopStacks,
    decimal TotalExalts,
    decimal TotalDivines,
    int KnownOccupiedSlots,
    int UnknownOccupiedSlots,
    Rectangle ScreenBounds,
    string StashCropPath,
    IReadOnlyList<CurrencySlotDetection> Slots);

internal sealed record CurrencySlotDetection(
    int SlotIndex,
    Rectangle CropBounds,
    bool Occupied,
    string? ItemName,
    int? Quantity,
    decimal? Exalts,
    decimal? Divines,
    bool IsCustomMapped,
    bool IsCountOverridden,
    double CountConfidence = 1,
    string CountMethod = "unknown",
    Rectangle? OverlayCropBounds = null);

internal sealed record RuneStack(string ItemName, int Quantity, decimal Exalts, decimal Divines)
{
    public string DisplayText => $"{ItemName,-34} x{Quantity,4} {Exalts,10:0.##} ex / {Divines,8:0.####} div";
}

internal sealed record RuneScanResult(
    IReadOnlyList<RuneStack> TopStacks,
    IReadOnlyList<RuneUpgradeSuggestion> UpgradeSuggestions,
    decimal TotalExalts,
    decimal TotalDivines,
    int KnownOccupiedSlots,
    int UnknownOccupiedSlots,
    Rectangle ScreenBounds,
    string StashCropPath,
    IReadOnlyList<RuneSlotDetection> Slots);

internal sealed record RuneSlotDetection(
    int SlotIndex,
    Rectangle CropBounds,
    bool Occupied,
    string? ItemName,
    int? Quantity,
    decimal? Exalts,
    decimal? Divines,
    bool IsCustomMapped,
    bool IsCountOverridden,
    double CountConfidence = 1,
    string CountMethod = "unknown",
    Rectangle? OverlayCropBounds = null);

internal sealed record FixedStashStack(string ItemName, int Quantity, decimal Exalts, decimal Divines)
{
    public string DisplayText => $"{ItemName,-34} x{Quantity,4} {Exalts,10:0.##} ex / {Divines,8:0.####} div";
}

internal sealed record FixedStashScanResult(
    FixedStashScannerProfile Profile,
    IReadOnlyList<FixedStashStack> TopStacks,
    decimal TotalExalts,
    decimal TotalDivines,
    int KnownOccupiedSlots,
    int UnknownOccupiedSlots,
    Rectangle ScreenBounds,
    string StashCropPath,
    IReadOnlyList<FixedStashSlotDetection> Slots);

internal sealed record FixedStashSlotDetection(
    int SlotIndex,
    Rectangle CropBounds,
    bool Occupied,
    string? ItemName,
    int? Quantity,
    decimal? Exalts,
    decimal? Divines,
    bool IsCustomMapped,
    bool IsCountOverridden,
    double CountConfidence = 1,
    string CountMethod = "unknown",
    Rectangle? OverlayCropBounds = null);

internal sealed record RuneUpgradeSuggestion(
    string FromItemName,
    string ToItemName,
    int AvailableQuantity,
    int UpgradeCount,
    decimal CostExalts,
    decimal OutputExalts,
    decimal ProfitExalts,
    decimal CostDivines,
    decimal OutputDivines,
    decimal ProfitDivines)
{
    public bool IsProfitable => ProfitExalts > 0;
}

internal sealed record StashLayoutProfile(Rectangle DisplayCropRegion, Point SlotOffset)
{
    public static readonly StashLayoutProfile Normal = new(new Rectangle(25, 250, 1275, 1275), Point.Empty);

    public static readonly StashLayoutProfile Folder = new(new Rectangle(25, 400, 1275, 1180), Point.Empty);

    public static readonly StashLayoutProfile FolderFull = new(new Rectangle(25, 330, 1275, 1275), Point.Empty);

    public static readonly StashLayoutProfile NormalFromFolderMap = new(new Rectangle(25, 250, 1275, 1275), new Point(0, -80));

    public static readonly StashLayoutProfile FolderFromNormalMap = new(new Rectangle(25, 330, 1275, 1225), new Point(0, 80));
}
