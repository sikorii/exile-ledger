using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Poe2PriceChecker;

internal static class CountCropDebugSettings
{
    public static bool SaveCountDebugCrops { get; set; } = true;
}

internal static class CountCropTrainingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static CountCropSaveResult TrySaveDebugCrop(
        Bitmap source,
        Rectangle slotBounds,
        string mode,
        int slotIndex,
        int? guessedCount,
        string? countMethod,
        string? scanId,
        string? debugDirectory)
    {
        if (!CountCropDebugSettings.SaveCountDebugCrops || string.IsNullOrWhiteSpace(debugDirectory))
        {
            return CountCropSaveResult.Empty;
        }

        var safeMode = SafePathPart(mode);
        var safeScanId = string.IsNullOrWhiteSpace(scanId)
            ? DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff")
            : SafePathPart(scanId);
        var directory = Path.Combine(debugDirectory, "count-crops", safeMode, safeScanId);
        return TrySaveCropPair(
            source,
            slotBounds,
            directory,
            BuildFilePrefix(safeMode, slotIndex, guessedCount, countMethod),
            new CountCropMetadata(
                safeMode,
                slotIndex,
                guessedCount,
                null,
                SelectCountRegion(slotBounds, guessedCount),
                null,
                DateTimeOffset.UtcNow,
                scanId,
                countMethod));
    }

    public static CountCropSaveResult TrySavePreviewCrop(
        string sourceImagePath,
        Rectangle slotBounds,
        string mode,
        int slotIndex,
        int? guessedCount,
        string? countMethod)
    {
        if (!File.Exists(sourceImagePath))
        {
            return CountCropSaveResult.Empty;
        }

        using var source = CurrencyScanner.LoadBitmapWithoutFileLock(sourceImagePath);
        var safeMode = SafePathPart(mode);
        var directory = Path.Combine(AppPaths.DebugDirectory, "count-crops", safeMode, "previews");
        return TrySaveCropPair(
            source,
            slotBounds,
            directory,
            BuildFilePrefix(safeMode, slotIndex, guessedCount, countMethod),
            new CountCropMetadata(
                safeMode,
                slotIndex,
                guessedCount,
                null,
                SelectCountRegion(slotBounds, guessedCount),
                sourceImagePath,
                DateTimeOffset.UtcNow,
                "preview",
                countMethod));
    }

    public static CountCropSaveResult TrySaveLabeledSample(
        string sourceImagePath,
        Rectangle slotBounds,
        int correctedCount,
        string mode,
        int slotIndex,
        int? originalGuessedCount)
    {
        if (correctedCount <= 0 || !File.Exists(sourceImagePath))
        {
            return CountCropSaveResult.Empty;
        }

        using var source = CurrencyScanner.LoadBitmapWithoutFileLock(sourceImagePath);
        var safeMode = SafePathPart(mode);
        var directory = Path.Combine(
            AppPaths.TrainingDirectory,
            "count-crops",
            "labeled",
            correctedCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return TrySaveCropPair(
            source,
            slotBounds,
            directory,
            BuildFilePrefix(safeMode, slotIndex, originalGuessedCount, "manual", correctedCount),
            new CountCropMetadata(
                safeMode,
                slotIndex,
                originalGuessedCount,
                correctedCount,
                SelectCountRegion(slotBounds, correctedCount),
                sourceImagePath,
                DateTimeOffset.UtcNow,
                null,
                "manual-correction"));
    }

    private static CountCropSaveResult TrySaveCropPair(
        Bitmap source,
        Rectangle slotBounds,
        string directory,
        string filePrefix,
        CountCropMetadata metadata)
    {
        var region = SelectCountRegion(slotBounds, metadata.CorrectedCount ?? metadata.GuessedCount);
        if (!ContainsRectangle(source.Size, region))
        {
            return CountCropSaveResult.Empty;
        }

        Directory.CreateDirectory(directory);
        var rawPath = Path.Combine(directory, $"{filePrefix}-raw.png");
        var cleanedPath = Path.Combine(directory, $"{filePrefix}-cleaned.png");
        var metadataPath = Path.Combine(directory, $"{filePrefix}.json");

        using var raw = source.Clone(region, source.PixelFormat);
        using var cleaned = StackCountReader.PrepareCountForOcr(raw);
        CurrencyScanner.SaveBitmap(raw, rawPath);
        CurrencyScanner.SaveBitmap(cleaned, cleanedPath);

        var savedMetadata = metadata with { CropBounds = region };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(savedMetadata, JsonOptions) + Environment.NewLine);
        return new CountCropSaveResult(rawPath, cleanedPath, metadataPath);
    }

    private static Rectangle SelectCountRegion(Rectangle slotBounds, int? count)
    {
        var targetWidth = count?.ToString(System.Globalization.CultureInfo.InvariantCulture).Length switch
        {
            1 => 44,
            2 => 70,
            3 => 102,
            _ => 70
        };

        return StackCountReader.BuildCountRegions(slotBounds)
            .OrderBy(region => Math.Abs(region.Width - targetWidth))
            .ThenByDescending(region => region.Width)
            .First();
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

    private static string BuildFilePrefix(string mode, int slotIndex, int? guessedCount, string? countMethod, int? correctedCount = null)
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var guess = guessedCount is null
            ? "guess-unknown"
            : $"guess-{guessedCount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var corrected = correctedCount is null
            ? string.Empty
            : $"-label-{correctedCount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var method = string.IsNullOrWhiteSpace(countMethod)
            ? string.Empty
            : $"-{SafePathPart(countMethod)}";
        return $"{stamp}-{mode}-slot-{slotIndex:000}-{guess}{corrected}{method}";
    }

    private static string SafePathPart(string value)
    {
        var chars = value
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')
            .ToArray();
        var safe = new string(chars).Trim('-');
        return safe.Length == 0 ? "unknown" : safe;
    }
}

internal sealed record CountCropSaveResult(string? RawPath, string? CleanedPath, string? MetadataPath)
{
    public static readonly CountCropSaveResult Empty = new(null, null, null);

    public bool Saved => RawPath is not null && CleanedPath is not null;
}

internal sealed record CountCropMetadata(
    string ProfileName,
    int SlotIndex,
    int? GuessedCount,
    int? CorrectedCount,
    Rectangle CropBounds,
    string? SourceImagePath,
    DateTimeOffset TimestampUtc,
    string? ScanId,
    string? CountMethod);
