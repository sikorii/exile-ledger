using System.Globalization;
using System.Text.Json;

namespace Poe2PriceChecker;

internal sealed class Poe2ScoutPrices
{
    private const string BaseUrl = "https://poe2scout.com/api";
    private const string Realm = "poe2";
    private const string League = "Runes of Aldur";
    private const string UserAgent = "Exile Ledger price-source-compare (contact: sikorii/exile-ledger GitHub)";

    private readonly HttpClient _client;
    private readonly Dictionary<string, ScoutPriceSummary> _summariesByNormalizedName;
    private readonly Dictionary<string, ScoutPriceLookup> _detailsByApiId = new(StringComparer.OrdinalIgnoreCase);

    private Poe2ScoutPrices(HttpClient client, Dictionary<string, ScoutPriceSummary> summariesByNormalizedName)
    {
        _client = client;
        _summariesByNormalizedName = summariesByNormalizedName;
    }

    public static async Task<Poe2ScoutPrices> FetchAsync(CancellationToken cancellationToken)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

        var summariesByNormalizedName = new Dictionary<string, ScoutPriceSummary>(StringComparer.OrdinalIgnoreCase);
        var categories = await FetchCurrencyCategoriesAsync(client, cancellationToken).ConfigureAwait(false);
        foreach (var category in categories)
        {
            await AddCategorySummariesAsync(client, category, summariesByNormalizedName, cancellationToken).ConfigureAwait(false);
        }

        return new Poe2ScoutPrices(client, summariesByNormalizedName);
    }

    public async Task<ScoutPriceLookup?> TryGetPriceLookupAsync(string itemName, CancellationToken cancellationToken)
    {
        var normalized = PoeNinjaPrices.Normalize(itemName);
        if (!_summariesByNormalizedName.TryGetValue(normalized, out var summary))
        {
            return null;
        }

        if (_detailsByApiId.TryGetValue(summary.ApiId, out var cached))
        {
            return cached;
        }

        try
        {
            var detail = await FetchDetailAsync(summary, cancellationToken).ConfigureAwait(false);
            _detailsByApiId[summary.ApiId] = detail;
            return detail;
        }
        catch
        {
            var fallback = summary.ToLookup(null, "Scout category CurrentPrice; detail unavailable.");
            _detailsByApiId[summary.ApiId] = fallback;
            return fallback;
        }
    }

    private static async Task<IReadOnlyList<string>> FetchCurrencyCategoriesAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(client, BuildUri($"{Realm}/Leagues/{League}/Items/Categories"), cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("CurrencyCategories", out var categories) ||
            categories.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var apiIds = new List<string>();
        foreach (var category in categories.EnumerateArray())
        {
            if (TryGetString(category, "ApiId", out var apiId))
            {
                apiIds.Add(apiId);
            }
        }

        return apiIds;
    }

    private static async Task AddCategorySummariesAsync(
        HttpClient client,
        string category,
        Dictionary<string, ScoutPriceSummary> summariesByNormalizedName,
        CancellationToken cancellationToken)
    {
        var page = 1;
        var pages = 1;
        do
        {
            using var document = await GetJsonAsync(
                client,
                BuildUri($"{Realm}/Leagues/{League}/Currencies/ByCategory",
                    ("Category", category),
                    ("Page", page.ToString(CultureInfo.InvariantCulture)),
                    ("PerPage", "250")),
                cancellationToken).ConfigureAwait(false);

            if (document.RootElement.TryGetProperty("Pages", out var pagesElement) &&
                pagesElement.TryGetInt32(out var parsedPages) &&
                parsedPages > 0)
            {
                pages = parsedPages;
            }

            if (document.RootElement.TryGetProperty("Items", out var items) &&
                items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    AddSummary(item, summariesByNormalizedName);
                }
            }

            page++;
        }
        while (page <= pages);
    }

    private static void AddSummary(JsonElement item, Dictionary<string, ScoutPriceSummary> summariesByNormalizedName)
    {
        if (!TryGetString(item, "Text", out var text) ||
            !TryGetString(item, "ApiId", out var apiId))
        {
            return;
        }

        TryGetString(item, "CategoryApiId", out var categoryApiId);
        var normalized = PoeNinjaPrices.Normalize(text);
        var summary = new ScoutPriceSummary(
            text,
            normalized,
            apiId,
            categoryApiId,
            TryGetDecimal(item, "CurrentPrice"),
            TryGetInt32(item, "CurrentQuantity"),
            TryGetFirstLogTime(item));

        if (!summariesByNormalizedName.TryGetValue(normalized, out var existing) ||
            IsBetterSummary(summary, existing))
        {
            summariesByNormalizedName[normalized] = summary;
        }
    }

    private static bool IsBetterSummary(ScoutPriceSummary candidate, ScoutPriceSummary existing)
    {
        var candidateHasPrice = candidate.CurrentPrice is > 0;
        var existingHasPrice = existing.CurrentPrice is > 0;
        if (candidateHasPrice != existingHasPrice)
        {
            return candidateHasPrice;
        }

        if (candidate.CurrentQuantity.GetValueOrDefault() > existing.CurrentQuantity.GetValueOrDefault())
        {
            return true;
        }

        return false;
    }

    private async Task<ScoutPriceLookup> FetchDetailAsync(ScoutPriceSummary summary, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            _client,
            BuildUri($"{Realm}/Leagues/{League}/Currencies/{summary.ApiId}"),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var price = TryGetDecimal(document.RootElement, "CurrentPrice") ?? summary.CurrentPrice;
        var quantity = TryGetFirstLogQuantity(document.RootElement) ?? summary.CurrentQuantity;
        var logTime = TryGetFirstLogTime(document.RootElement) ?? summary.LogTime;
        var note = price == summary.CurrentPrice
            ? "Scout Currencies/{ApiId}.CurrentPrice."
            : "Scout detail CurrentPrice differed from category CurrentPrice.";

        return new ScoutPriceLookup(
            summary.Name,
            summary.NormalizedName,
            summary.ApiId,
            summary.CategoryApiId,
            price,
            quantity,
            logTime,
            note);
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string uri, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string BuildUri(string path, params (string Name, string Value)[] query)
    {
        var encodedPath = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
        if (query.Length == 0)
        {
            return $"{BaseUrl}/{encodedPath}";
        }

        var queryString = string.Join("&", query.Select(parameter =>
            $"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(parameter.Value)}"));
        return $"{BaseUrl}/{encodedPath}?{queryString}";
    }

    private static bool TryGetString(JsonElement item, string propertyName, out string value)
    {
        value = string.Empty;
        if (!item.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString() ?? string.Empty;
        }
        else if (element.ValueKind == JsonValueKind.Number)
        {
            value = element.GetRawText();
        }

        value = value.Trim();
        return value.Length > 0;
    }

    private static decimal? TryGetDecimal(JsonElement item, string propertyName)
    {
        if (item.TryGetProperty(propertyName, out var element) &&
            element.ValueKind != JsonValueKind.Null &&
            element.TryGetDecimal(out var value))
        {
            return value;
        }

        return null;
    }

    private static int? TryGetInt32(JsonElement item, string propertyName)
    {
        if (item.TryGetProperty(propertyName, out var element) &&
            element.ValueKind != JsonValueKind.Null &&
            element.TryGetInt32(out var value))
        {
            return value;
        }

        return null;
    }

    private static DateTimeOffset? TryGetFirstLogTime(JsonElement item)
    {
        if (!item.TryGetProperty("PriceLogs", out var logs) ||
            logs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var log in logs.EnumerateArray())
        {
            if (log.TryGetProperty("Time", out var timeElement) &&
                timeElement.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(timeElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
            {
                return time;
            }

            break;
        }

        return null;
    }

    private static int? TryGetFirstLogQuantity(JsonElement item)
    {
        if (!item.TryGetProperty("PriceLogs", out var logs) ||
            logs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var log in logs.EnumerateArray())
        {
            return TryGetInt32(log, "Quantity");
        }

        return null;
    }
}

internal sealed record ScoutPriceSummary(
    string Name,
    string NormalizedName,
    string ApiId,
    string CategoryApiId,
    decimal? CurrentPrice,
    int? CurrentQuantity,
    DateTimeOffset? LogTime)
{
    public ScoutPriceLookup ToLookup(DateTimeOffset? detailLogTime, string note)
    {
        return new ScoutPriceLookup(
            Name,
            NormalizedName,
            ApiId,
            CategoryApiId,
            CurrentPrice,
            CurrentQuantity,
            detailLogTime ?? LogTime,
            note);
    }
}

internal sealed record ScoutPriceLookup(
    string Name,
    string NormalizedName,
    string ApiId,
    string CategoryApiId,
    decimal? Exalts,
    int? Quantity,
    DateTimeOffset? LogTime,
    string Note);
