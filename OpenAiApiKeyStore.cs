using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Poe2PriceChecker;

internal enum OpenAiApiKeyConfigurationSource
{
    Stored,
    Environment,
    None
}

internal sealed class OpenAiApiKeyStore
{
    private const string EnvironmentVariableName = "OPENAI_API_KEY";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _secretsPath;

    public OpenAiApiKeyStore(string secretsPath)
    {
        _secretsPath = secretsPath;
    }

    public string SecretsPath => _secretsPath;

    public async Task SaveOpenAiApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key cannot be empty.", nameof(apiKey));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_secretsPath)!);

        var plaintextBytes = Encoding.UTF8.GetBytes(apiKey.Trim());
        var protectedBytes = ProtectedData.Protect(
            plaintextBytes,
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        var file = new SecretSettingsFile(Convert.ToBase64String(protectedBytes));

        await File.WriteAllTextAsync(
            _secretsPath,
            JsonSerializer.Serialize(file, JsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    public bool TryGetOpenAiApiKey(out string apiKey)
    {
        var stored = TryReadStoredOpenAiApiKey();
        if (stored.ApiKey is not null)
        {
            apiKey = stored.ApiKey;
            return true;
        }

        var environmentKey = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentKey))
        {
            apiKey = environmentKey;
            return true;
        }

        apiKey = string.Empty;
        return false;
    }

    public async Task ClearOpenAiApiKeyAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            if (File.Exists(_secretsPath))
            {
                File.Delete(_secretsPath);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public bool HasStoredOpenAiApiKey()
    {
        return TryReadStoredOpenAiApiKey().ApiKey is not null;
    }

    public bool StoredOpenAiApiKeyCouldNotBeRead()
    {
        var result = TryReadStoredOpenAiApiKey();
        return result.FileExists && result.ApiKey is null && result.ReadFailed;
    }

    public OpenAiApiKeyConfigurationSource GetConfiguredSource()
    {
        var stored = TryReadStoredOpenAiApiKey();
        if (stored.ApiKey is not null)
        {
            return OpenAiApiKeyConfigurationSource.Stored;
        }

        return string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariableName))
            ? OpenAiApiKeyConfigurationSource.None
            : OpenAiApiKeyConfigurationSource.Environment;
    }

    private StoredOpenAiApiKeyReadResult TryReadStoredOpenAiApiKey()
    {
        if (!File.Exists(_secretsPath))
        {
            return new StoredOpenAiApiKeyReadResult(false, false, null);
        }

        try
        {
            var json = File.ReadAllText(_secretsPath);
            var file = JsonSerializer.Deserialize<SecretSettingsFile>(json);
            if (string.IsNullOrWhiteSpace(file?.OpenAiApiKey))
            {
                return new StoredOpenAiApiKeyReadResult(true, true, null);
            }

            var protectedBytes = Convert.FromBase64String(file.OpenAiApiKey);
            var plaintextBytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            var apiKey = Encoding.UTF8.GetString(plaintextBytes);

            return string.IsNullOrWhiteSpace(apiKey)
                ? new StoredOpenAiApiKeyReadResult(true, true, null)
                : new StoredOpenAiApiKeyReadResult(true, false, apiKey);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or CryptographicException)
        {
            return new StoredOpenAiApiKeyReadResult(true, true, null);
        }
    }

    private sealed record SecretSettingsFile(string OpenAiApiKey);

    private sealed record StoredOpenAiApiKeyReadResult(
        bool FileExists,
        bool ReadFailed,
        string? ApiKey);
}
