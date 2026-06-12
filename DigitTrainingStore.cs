using System.Text.Json;
using System.Text.Json.Serialization;

namespace Poe2PriceChecker;

internal sealed class DigitTrainingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _directory;
    private readonly string _sampleDirectory;
    private readonly string _indexPath;

    public DigitTrainingStore(string directory)
    {
        _directory = directory;
        _sampleDirectory = Path.Combine(directory, "samples");
        _indexPath = Path.Combine(directory, "digit-training.json");
    }

    public static DigitTrainingStore CreateDefault()
    {
        return new DigitTrainingStore(Path.Combine(AppContext.BaseDirectory, "config", "digit-training"));
    }

    public IReadOnlyList<DigitTrainingSample> LoadSamples()
    {
        if (!File.Exists(_indexPath))
        {
            return [];
        }

        try
        {
            var index = JsonSerializer.Deserialize<DigitTrainingIndex>(File.ReadAllText(_indexPath), JsonOptions);
            if (index?.Samples is null)
            {
                return [];
            }

            return index.Samples
                .Where(sample => sample.Digit is >= '0' and <= '9')
                .Where(sample => File.Exists(GetAbsolutePath(sample.RelativePath)))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public bool SaveSample(char digit, Bitmap image, string source)
    {
        if (digit is < '0' or > '9')
        {
            return false;
        }

        var samples = LoadSamples().ToList();
        if (samples.Any(sample =>
            sample.Digit == digit &&
            sample.Source.Equals(source, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        Directory.CreateDirectory(_sampleDirectory);
        var fileName = $"{digit}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.png";
        var absolutePath = Path.Combine(_sampleDirectory, fileName);
        CurrencyScanner.SaveBitmap(image, absolutePath);

        samples.Add(new DigitTrainingSample(
            digit,
            Path.Combine("samples", fileName),
            source,
            DateTimeOffset.UtcNow));

        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            _indexPath,
            JsonSerializer.Serialize(new DigitTrainingIndex(samples), JsonOptions));
        return true;
    }

    public string GetAbsolutePath(string relativePath)
    {
        return Path.GetFullPath(Path.Combine(_directory, relativePath));
    }

    private sealed record DigitTrainingIndex(IReadOnlyList<DigitTrainingSample> Samples);
}

internal sealed record DigitTrainingSample(
    char Digit,
    string RelativePath,
    string Source,
    DateTimeOffset CreatedUtc);
