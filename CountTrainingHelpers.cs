namespace Poe2PriceChecker;

internal static class CountTrainingHelpers
{
    public static string TrySaveFromOverride(
        Bitmap image,
        Rectangle slotBounds,
        int? countOverride,
        string mode,
        int slotIndex,
        string debugDirectory)
    {
        if (countOverride is null or <= 0)
        {
            return string.Empty;
        }

        try
        {
            var coordinateScale = ScreenshotResolutionProfile.DetectScaleOrDefault(image.Size);
            var result = StackCountReader.SaveTrainingSamplesFromOverride(
                image,
                slotBounds,
                countOverride.Value,
                mode,
                slotIndex,
                debugDirectory,
                coordinateScale);
            return result.SamplesSaved > 0
                ? $" trained {result.SamplesSaved} digit sample{(result.SamplesSaved == 1 ? string.Empty : "s")}"
                : $" training skipped: {result.Message}";
        }
        catch (Exception ex)
        {
            return $" training skipped: {ex.Message}";
        }
    }

    public static string TrySaveFromOverride(
        string imagePath,
        Rectangle slotBounds,
        int? countOverride,
        string mode,
        int slotIndex,
        string debugDirectory)
    {
        if (countOverride is null || !File.Exists(imagePath))
        {
            return string.Empty;
        }

        using var image = CurrencyScanner.LoadBitmapWithoutFileLock(imagePath);
        return TrySaveFromOverride(image, slotBounds, countOverride, mode, slotIndex, debugDirectory);
    }

    public static string TrySaveFromManualCorrection(
        string imagePath,
        Rectangle slotBounds,
        int? countOverride,
        int? originalGuessedCount,
        string mode,
        int slotIndex,
        string debugDirectory)
    {
        if (countOverride is null || !File.Exists(imagePath))
        {
            return string.Empty;
        }

        var digitTrainingStatus = TrySaveFromOverride(
            imagePath,
            slotBounds,
            countOverride,
            mode,
            slotIndex,
            debugDirectory);

        var cropStatus = string.Empty;
        if (countOverride > 0)
        {
            try
            {
                var labeled = CountCropTrainingStore.TrySaveLabeledSample(
                    imagePath,
                    slotBounds,
                    countOverride.Value,
                    mode,
                    slotIndex,
                    originalGuessedCount);
                cropStatus = labeled.Saved
                    ? " saved labeled count crop"
                    : string.Empty;
            }
            catch (Exception ex)
            {
                cropStatus = $" labeled count crop skipped: {ex.Message}";
            }
        }

        if (digitTrainingStatus.Length == 0)
        {
            return cropStatus.Trim();
        }

        return cropStatus.Length == 0
            ? digitTrainingStatus
            : $"{digitTrainingStatus};{cropStatus}";
    }
}
