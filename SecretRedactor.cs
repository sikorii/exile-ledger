namespace Poe2PriceChecker;

internal static class SecretRedactor
{
    public static string Redact(string value, params string?[] secrets)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var redacted = value;
        foreach (var secret in secrets)
        {
            if (string.IsNullOrWhiteSpace(secret))
            {
                continue;
            }

            redacted = redacted.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        }

        return redacted;
    }
}
