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
            var result = StackCountReader.SaveTrainingSamplesFromOverride(
                image,
                slotBounds,
                countOverride.Value,
                mode,
                slotIndex,
                debugDirectory);
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
}
