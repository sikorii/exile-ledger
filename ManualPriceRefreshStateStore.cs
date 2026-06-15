using System.Text.Json;
using System.Text.Json.Serialization;

namespace Poe2PriceChecker;

internal sealed class ManualPriceRefreshStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public ManualPriceRefreshStateStore(string path)
    {
        _path = path;
    }

    public DateTimeOffset? LoadLastSuccessfulManualPriceRefreshUtc()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(_path);
            var document = JsonSerializer.Deserialize<RefreshStateFile>(stream, JsonOptions);
            return document?.LastSuccessfulManualPriceRefreshUtc?.ToUniversalTime();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public bool TrySaveLastSuccessfulManualPriceRefreshUtc(DateTimeOffset timestampUtc, out Exception? error)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var document = new RefreshStateFile(timestampUtc.ToUniversalTime());
            var tempPath = Path.Combine(
                directory ?? AppContext.BaseDirectory,
                $"{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");

            File.WriteAllText(tempPath, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(tempPath, _path, overwrite: true);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = ex;
            return false;
        }
    }

    private sealed record RefreshStateFile(
        [property: JsonPropertyName("lastSuccessfulManualPriceRefreshUtc")]
        DateTimeOffset? LastSuccessfulManualPriceRefreshUtc);
}
