using System.Text.Json;
using System.Text.Json.Serialization;

namespace Poe2PriceChecker;

internal sealed class LatestStashScanStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;

    public LatestStashScanStore(string path)
    {
        _path = path;
    }

    public LatestStashScanSnapshot Load(IReadOnlyList<FixedStashScannerProfile> profiles)
    {
        if (!File.Exists(_path))
        {
            return LatestStashScanSnapshot.Empty;
        }

        try
        {
            using var stream = File.OpenRead(_path);
            var document = JsonSerializer.Deserialize<PersistedLatestStashScans>(stream, JsonOptions);
            if (document is null)
            {
                return LatestStashScanSnapshot.Empty;
            }

            var profileByKey = profiles.ToDictionary(profile => profile.Key, StringComparer.OrdinalIgnoreCase);
            var currency = document.Currency.ToDictionary(
                pair => pair.Key,
                pair => ToCurrencyResult(pair.Value),
                StringComparer.OrdinalIgnoreCase);
            var runes = document.Runes.ToDictionary(
                pair => pair.Key,
                pair => ToRuneResult(pair.Value),
                StringComparer.OrdinalIgnoreCase);
            var generic = new Dictionary<string, FixedStashScanResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in document.Generic)
            {
                if (profileByKey.TryGetValue(pair.Value.ProfileKey, out var profile))
                {
                    generic[pair.Key] = ToGenericResult(pair.Value, profile);
                }
            }

            return new LatestStashScanSnapshot(currency, runes, generic);
        }
        catch
        {
            return LatestStashScanSnapshot.Empty;
        }
    }

    public void Save(
        IReadOnlyDictionary<string, CurrencyScanResult> currency,
        IReadOnlyDictionary<string, RuneScanResult> runes,
        IReadOnlyDictionary<string, FixedStashScanResult> generic)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new PersistedLatestStashScans(
            DateTimeOffset.UtcNow,
            currency.ToDictionary(pair => pair.Key, pair => From(pair.Value), StringComparer.OrdinalIgnoreCase),
            runes.ToDictionary(pair => pair.Key, pair => From(pair.Value), StringComparer.OrdinalIgnoreCase),
            generic.ToDictionary(pair => pair.Key, pair => From(pair.Value), StringComparer.OrdinalIgnoreCase));

        var tempPath = Path.Combine(
            directory ?? AppContext.BaseDirectory,
            $"{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");

        File.WriteAllText(tempPath, JsonSerializer.Serialize(document, JsonOptions));
        File.Move(tempPath, _path, overwrite: true);
    }

    private static PersistedCurrencyScanResult From(CurrencyScanResult result)
    {
        return new PersistedCurrencyScanResult(
            result.TopStacks.Select(stack => new PersistedStack(stack.ItemName, stack.Quantity, stack.Exalts, stack.Divines)).ToArray(),
            result.TotalExalts,
            result.TotalDivines,
            result.KnownOccupiedSlots,
            result.UnknownOccupiedSlots,
            Rect.From(result.ScreenBounds),
            result.StashCropPath,
            result.Slots.Select(From).ToArray());
    }

    private static PersistedRuneScanResult From(RuneScanResult result)
    {
        return new PersistedRuneScanResult(
            result.TopStacks.Select(stack => new PersistedStack(stack.ItemName, stack.Quantity, stack.Exalts, stack.Divines)).ToArray(),
            result.UpgradeSuggestions.Select(From).ToArray(),
            result.TotalExalts,
            result.TotalDivines,
            result.KnownOccupiedSlots,
            result.UnknownOccupiedSlots,
            Rect.From(result.ScreenBounds),
            result.StashCropPath,
            result.Slots.Select(From).ToArray());
    }

    private static PersistedGenericScanResult From(FixedStashScanResult result)
    {
        return new PersistedGenericScanResult(
            result.Profile.Key,
            result.TopStacks.Select(stack => new PersistedStack(stack.ItemName, stack.Quantity, stack.Exalts, stack.Divines)).ToArray(),
            result.TotalExalts,
            result.TotalDivines,
            result.KnownOccupiedSlots,
            result.UnknownOccupiedSlots,
            Rect.From(result.ScreenBounds),
            result.StashCropPath,
            result.Slots.Select(From).ToArray());
    }

    private static PersistedCurrencySlot From(CurrencySlotDetection slot)
    {
        return new PersistedCurrencySlot(
            slot.SlotIndex,
            Rect.From(slot.CropBounds),
            slot.Occupied,
            slot.ItemName,
            slot.Quantity,
            slot.Exalts,
            slot.Divines,
            slot.IsCustomMapped,
            slot.IsCountOverridden,
            slot.CountConfidence,
            slot.CountMethod,
            Rect.From(slot.OverlayCropBounds ?? slot.CropBounds));
    }

    private static PersistedRuneSlot From(RuneSlotDetection slot)
    {
        return new PersistedRuneSlot(
            slot.SlotIndex,
            Rect.From(slot.CropBounds),
            slot.Occupied,
            slot.ItemName,
            slot.Quantity,
            slot.Exalts,
            slot.Divines,
            slot.IsCustomMapped,
            slot.IsCountOverridden,
            slot.CountConfidence,
            slot.CountMethod,
            Rect.From(slot.OverlayCropBounds ?? slot.CropBounds));
    }

    private static PersistedGenericSlot From(FixedStashSlotDetection slot)
    {
        return new PersistedGenericSlot(
            slot.SlotIndex,
            Rect.From(slot.CropBounds),
            slot.Occupied,
            slot.ItemName,
            slot.Quantity,
            slot.Exalts,
            slot.Divines,
            slot.IsCustomMapped,
            slot.IsCountOverridden,
            slot.CountConfidence,
            slot.CountMethod,
            Rect.From(slot.OverlayCropBounds ?? slot.CropBounds));
    }

    private static PersistedUpgrade From(RuneUpgradeSuggestion suggestion)
    {
        return new PersistedUpgrade(
            suggestion.FromItemName,
            suggestion.ToItemName,
            suggestion.AvailableQuantity,
            suggestion.UpgradeCount,
            suggestion.CostExalts,
            suggestion.OutputExalts,
            suggestion.ProfitExalts,
            suggestion.CostDivines,
            suggestion.OutputDivines,
            suggestion.ProfitDivines);
    }

    private static CurrencyScanResult ToCurrencyResult(PersistedCurrencyScanResult result)
    {
        return new CurrencyScanResult(
            result.TopStacks.Select(stack => new CurrencyStack(stack.ItemName, stack.Quantity, stack.Exalts, stack.Divines)).ToArray(),
            result.TotalExalts,
            result.TotalDivines,
            result.KnownOccupiedSlots,
            result.UnknownOccupiedSlots,
            result.ScreenBounds.ToRectangle(),
            result.StashCropPath,
            result.Slots.Select(ToCurrencySlot).ToArray());
    }

    private static RuneScanResult ToRuneResult(PersistedRuneScanResult result)
    {
        return new RuneScanResult(
            result.TopStacks.Select(stack => new RuneStack(stack.ItemName, stack.Quantity, stack.Exalts, stack.Divines)).ToArray(),
            result.UpgradeSuggestions.Select(ToUpgrade).ToArray(),
            result.TotalExalts,
            result.TotalDivines,
            result.KnownOccupiedSlots,
            result.UnknownOccupiedSlots,
            result.ScreenBounds.ToRectangle(),
            result.StashCropPath,
            result.Slots.Select(ToRuneSlot).ToArray());
    }

    private static FixedStashScanResult ToGenericResult(PersistedGenericScanResult result, FixedStashScannerProfile profile)
    {
        return new FixedStashScanResult(
            profile,
            result.TopStacks.Select(stack => new FixedStashStack(stack.ItemName, stack.Quantity, stack.Exalts, stack.Divines)).ToArray(),
            result.TotalExalts,
            result.TotalDivines,
            result.KnownOccupiedSlots,
            result.UnknownOccupiedSlots,
            result.ScreenBounds.ToRectangle(),
            result.StashCropPath,
            result.Slots.Select(ToGenericSlot).ToArray());
    }

    private static CurrencySlotDetection ToCurrencySlot(PersistedCurrencySlot slot)
    {
        return new CurrencySlotDetection(
            slot.SlotIndex,
            slot.CropBounds.ToRectangle(),
            slot.Occupied,
            slot.ItemName,
            slot.Quantity,
            slot.Exalts,
            slot.Divines,
            slot.IsCustomMapped,
            slot.IsCountOverridden,
            slot.CountConfidence,
            slot.CountMethod,
            slot.OverlayCropBounds?.ToRectangle());
    }

    private static RuneSlotDetection ToRuneSlot(PersistedRuneSlot slot)
    {
        return new RuneSlotDetection(
            slot.SlotIndex,
            slot.CropBounds.ToRectangle(),
            slot.Occupied,
            slot.ItemName,
            slot.Quantity,
            slot.Exalts,
            slot.Divines,
            slot.IsCustomMapped,
            slot.IsCountOverridden,
            slot.CountConfidence,
            slot.CountMethod,
            slot.OverlayCropBounds?.ToRectangle());
    }

    private static FixedStashSlotDetection ToGenericSlot(PersistedGenericSlot slot)
    {
        return new FixedStashSlotDetection(
            slot.SlotIndex,
            slot.CropBounds.ToRectangle(),
            slot.Occupied,
            slot.ItemName,
            slot.Quantity,
            slot.Exalts,
            slot.Divines,
            slot.IsCustomMapped,
            slot.IsCountOverridden,
            slot.CountConfidence,
            slot.CountMethod,
            slot.OverlayCropBounds?.ToRectangle());
    }

    private static RuneUpgradeSuggestion ToUpgrade(PersistedUpgrade suggestion)
    {
        return new RuneUpgradeSuggestion(
            suggestion.FromItemName,
            suggestion.ToItemName,
            suggestion.AvailableQuantity,
            suggestion.UpgradeCount,
            suggestion.CostExalts,
            suggestion.OutputExalts,
            suggestion.ProfitExalts,
            suggestion.CostDivines,
            suggestion.OutputDivines,
            suggestion.ProfitDivines);
    }

    private sealed record PersistedLatestStashScans(
        DateTimeOffset SavedUtc,
        Dictionary<string, PersistedCurrencyScanResult> Currency,
        Dictionary<string, PersistedRuneScanResult> Runes,
        Dictionary<string, PersistedGenericScanResult> Generic);

    private sealed record PersistedCurrencyScanResult(
        IReadOnlyList<PersistedStack> TopStacks,
        decimal TotalExalts,
        decimal TotalDivines,
        int KnownOccupiedSlots,
        int UnknownOccupiedSlots,
        Rect ScreenBounds,
        string StashCropPath,
        IReadOnlyList<PersistedCurrencySlot> Slots);

    private sealed record PersistedRuneScanResult(
        IReadOnlyList<PersistedStack> TopStacks,
        IReadOnlyList<PersistedUpgrade> UpgradeSuggestions,
        decimal TotalExalts,
        decimal TotalDivines,
        int KnownOccupiedSlots,
        int UnknownOccupiedSlots,
        Rect ScreenBounds,
        string StashCropPath,
        IReadOnlyList<PersistedRuneSlot> Slots);

    private sealed record PersistedGenericScanResult(
        string ProfileKey,
        IReadOnlyList<PersistedStack> TopStacks,
        decimal TotalExalts,
        decimal TotalDivines,
        int KnownOccupiedSlots,
        int UnknownOccupiedSlots,
        Rect ScreenBounds,
        string StashCropPath,
        IReadOnlyList<PersistedGenericSlot> Slots);

    private sealed record PersistedStack(string ItemName, int Quantity, decimal Exalts, decimal Divines);

    private sealed record PersistedUpgrade(
        string FromItemName,
        string ToItemName,
        int AvailableQuantity,
        int UpgradeCount,
        decimal CostExalts,
        decimal OutputExalts,
        decimal ProfitExalts,
        decimal CostDivines,
        decimal OutputDivines,
        decimal ProfitDivines);

    private sealed record PersistedCurrencySlot(
        int SlotIndex,
        Rect CropBounds,
        bool Occupied,
        string? ItemName,
        int? Quantity,
        decimal? Exalts,
        decimal? Divines,
        bool IsCustomMapped,
        bool IsCountOverridden,
        double CountConfidence,
        string CountMethod,
        Rect? OverlayCropBounds = null);

    private sealed record PersistedRuneSlot(
        int SlotIndex,
        Rect CropBounds,
        bool Occupied,
        string? ItemName,
        int? Quantity,
        decimal? Exalts,
        decimal? Divines,
        bool IsCustomMapped,
        bool IsCountOverridden,
        double CountConfidence,
        string CountMethod,
        Rect? OverlayCropBounds = null);

    private sealed record PersistedGenericSlot(
        int SlotIndex,
        Rect CropBounds,
        bool Occupied,
        string? ItemName,
        int? Quantity,
        decimal? Exalts,
        decimal? Divines,
        bool IsCustomMapped,
        bool IsCountOverridden,
        double CountConfidence,
        string CountMethod,
        Rect? OverlayCropBounds = null);

    private sealed record Rect(int X, int Y, int Width, int Height)
    {
        public static Rect From(Rectangle rectangle)
        {
            return new Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
        }

        public Rectangle ToRectangle()
        {
            return new Rectangle(X, Y, Width, Height);
        }
    }
}

internal sealed record LatestStashScanSnapshot(
    IReadOnlyDictionary<string, CurrencyScanResult> Currency,
    IReadOnlyDictionary<string, RuneScanResult> Runes,
    IReadOnlyDictionary<string, FixedStashScanResult> Generic)
{
    public static readonly LatestStashScanSnapshot Empty = new(
        new Dictionary<string, CurrencyScanResult>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, RuneScanResult>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, FixedStashScanResult>(StringComparer.OrdinalIgnoreCase));
}
