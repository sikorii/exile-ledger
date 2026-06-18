using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Poe2PriceChecker;

internal sealed class PoeNinjaPrices
{
    private const string League = "Runes of Aldur";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static PoeNinjaPrices? _memoryCache;
    private static DateTimeOffset _lastLiveFetchUtc = DateTimeOffset.MinValue;

    private static readonly string[] ExchangeTypes =
    [
        "Currency",
        "Expedition",
        "UncutGems",
        "Runes",
        "Verisium",
        "Abyss",
        "Delirium",
        "Ritual",
        "Breach",
        "SoulCores",
        "Idols",
        "Essences",
        "Fragments"
    ];

    private readonly Dictionary<string, PriceCacheItem> _itemsByNormalizedName;
    private readonly IReadOnlyList<string> _knownItemNames;
    private readonly PriceCacheDocument _cacheDocument;

    private PoeNinjaPrices(
        Dictionary<string, PriceCacheItem> itemsByNormalizedName,
        IReadOnlyList<string> knownItemNames,
        PriceCacheDocument cacheDocument)
    {
        _itemsByNormalizedName = itemsByNormalizedName;
        _knownItemNames = knownItemNames;
        _cacheDocument = cacheDocument;
    }

    public IReadOnlyList<string> KnownItemNames => _knownItemNames;

    public PriceCacheSummary CacheSummary => new(
        _cacheDocument.FetchedUtc,
        _cacheDocument.League,
        _cacheDocument.Items.Count,
        _cacheDocument.Categories.Select(category => category.Type).ToArray(),
        _cacheDocument.FailedCategories);

    public static async Task<PoeNinjaPrices> FetchAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        await RefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _memoryCache is not null && IsFresh(_memoryCache._cacheDocument.FetchedUtc))
            {
                return _memoryCache;
            }

            var cachePath = CachePath;
            if (!forceRefresh && TryLoadFromDisk(cachePath, out var cached) && IsFresh(cached._cacheDocument.FetchedUtc))
            {
                _memoryCache = cached;
                return cached;
            }

            if (forceRefresh &&
                _memoryCache is not null &&
                DateTimeOffset.UtcNow - _lastLiveFetchUtc < TimeSpan.FromSeconds(20))
            {
                return _memoryCache;
            }

            try
            {
                var live = await FetchLiveAsync(cancellationToken).ConfigureAwait(false);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                await File.WriteAllTextAsync(
                    cachePath,
                    JsonSerializer.Serialize(live._cacheDocument, JsonOptions),
                    cancellationToken).ConfigureAwait(false);
                _memoryCache = live;
                _lastLiveFetchUtc = DateTimeOffset.UtcNow;
                return live;
            }
            catch
            {
                if (TryLoadFromDisk(cachePath, out var fallback))
                {
                    _memoryCache = fallback;
                    return fallback;
                }

                throw;
            }
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    public MarketValue? TryGetValue(string itemName, int quantity)
    {
        if (!_itemsByNormalizedName.TryGetValue(Normalize(itemName), out var item))
        {
            return null;
        }

        return new MarketValue(item.Exalts * quantity, item.Divines * quantity);
    }

    public PoeNinjaPriceLookup? TryGetPriceLookup(string itemName)
    {
        if (!_itemsByNormalizedName.TryGetValue(Normalize(itemName), out var item))
        {
            return null;
        }

        return new PoeNinjaPriceLookup(
            item.Name,
            item.NormalizedName,
            item.Category,
            item.Exalts,
            item.Divines);
    }

    public PriceLookupDiagnostic DiagnoseMissing(string itemName, IReadOnlySet<string>? expectedCategories = null)
    {
        var normalized = Normalize(itemName);
        var fetchedCategories = _cacheDocument.Categories.Select(category => category.Type).ToArray();
        var expected = expectedCategories?.ToArray() ?? [];
        var expectedFetched = expected.Length == 0 ||
            expected.Any(expectedCategory => fetchedCategories.Any(fetched =>
                fetched.Equals(expectedCategory, StringComparison.OrdinalIgnoreCase)));

        var closeMatches = _itemsByNormalizedName.Values
            .Select(item => new
            {
                Item = item,
                Distance = EditDistance(normalized, item.NormalizedName)
            })
            .Where(match => match.Distance <= Math.Max(3, normalized.Length / 3))
            .OrderBy(match => match.Distance)
            .ThenBy(match => match.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .Select(match => $"{match.Item.Name} [{match.Item.Category}]")
            .ToArray();

        return new PriceLookupDiagnostic(
            itemName,
            normalized,
            expected,
            fetchedCategories,
            _cacheDocument.FailedCategories,
            expectedFetched,
            closeMatches);
    }

    internal static string Normalize(string name)
    {
        var withoutPossessiveBreaks = name.Replace("'", string.Empty).Replace("’", string.Empty);
        return Regex.Replace(withoutPossessiveBreaks, @"[^A-Za-z0-9]+", " ").Trim().ToUpperInvariant();
    }

    private static async Task<PoeNinjaPrices> FetchLiveAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("POE2-Price-Checker/0.1");

        var itemsByNormalizedName = new Dictionary<string, PriceCacheItem>(StringComparer.OrdinalIgnoreCase);
        var categories = new List<PriceCacheCategory>();
        var failedCategories = new List<string>();

        foreach (var type in ExchangeTypes)
        {
            try
            {
                var count = await AddExchangePricesAsync(client, type, itemsByNormalizedName, cancellationToken).ConfigureAwait(false);
                categories.Add(new PriceCacheCategory(type, count));
            }
            catch (Exception ex)
            {
                failedCategories.Add($"{type}: {ex.Message}");
            }
        }

        var document = new PriceCacheDocument(
            DateTimeOffset.UtcNow,
            League,
            categories,
            failedCategories,
            itemsByNormalizedName.Values
                .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray());

        return FromDocument(document);
    }

    private static async Task<int> AddExchangePricesAsync(
        HttpClient client,
        string type,
        Dictionary<string, PriceCacheItem> itemsByNormalizedName,
        CancellationToken cancellationToken)
    {
        var uri = $"https://poe.ninja/poe2/api/economy/exchange/current/overview?league={Uri.EscapeDataString(League)}&type={Uri.EscapeDataString(type)}";
        using var response = await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var converter = MarketValueConverter.From(document.RootElement);
        var itemNamesById = BuildItemNamesById(document.RootElement);

        if (!document.RootElement.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var lineCount = 0;
        foreach (var line in lines.EnumerateArray())
        {
            if (!TryReadPrimaryValue(line, out var primaryValue))
            {
                continue;
            }

            lineCount++;
            var value = converter.ToMarketValue(primaryValue);
            foreach (var name in CandidateNames(line, itemNamesById))
            {
                var normalized = Normalize(name);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                itemsByNormalizedName[normalized] = new PriceCacheItem(
                    name,
                    normalized,
                    type,
                    value.Exalts,
                    value.Divines);
            }
        }

        return lineCount;
    }

    private static Dictionary<string, string> BuildItemNamesById(JsonElement root)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddItems(root, "items", names);

        if (root.TryGetProperty("core", out var core) && core.ValueKind == JsonValueKind.Object)
        {
            AddItems(core, "items", names);
        }

        return names;
    }

    private static void AddItems(JsonElement root, string propertyName, Dictionary<string, string> names)
    {
        if (!root.TryGetProperty(propertyName, out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (!TryGetString(item, "name", out var name))
            {
                continue;
            }

            foreach (var idProperty in new[] { "id", "detailsId", "itemId" })
            {
                if (TryGetString(item, idProperty, out var id))
                {
                    names.TryAdd(id, name);
                }
            }
        }
    }

    private static IEnumerable<string> CandidateNames(JsonElement line, IReadOnlyDictionary<string, string> itemNamesById)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var propertyName in new[] { "currencyTypeName", "name", "baseType" })
        {
            if (TryGetString(line, propertyName, out var name) && seen.Add(name))
            {
                yield return name;
            }
        }

        if (!TryGetString(line, "id", out var id))
        {
            yield break;
        }

        if (itemNamesById.TryGetValue(id, out var catalogName) && seen.Add(catalogName))
        {
            yield return catalogName;
        }

        if (seen.Add(id))
        {
            yield return id;
        }

        var humanized = HumanizeSlug(id);
        if (seen.Add(humanized))
        {
            yield return humanized;
        }

        if (!IdAliases.TryGetValue(id, out var aliases))
        {
            yield break;
        }

        foreach (var alias in aliases)
        {
            if (seen.Add(alias))
            {
                yield return alias;
            }
        }
    }

    private static bool TryReadPrimaryValue(JsonElement line, out decimal primaryValue)
    {
        primaryValue = 0m;
        foreach (var propertyName in new[] { "primaryValue", "chaosEquivalent", "chaosValue" })
        {
            if (line.TryGetProperty(propertyName, out var value) &&
                value.TryGetDecimal(out primaryValue) &&
                primaryValue > 0)
            {
                return true;
            }
        }

        return false;
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

    private static PoeNinjaPrices FromDocument(PriceCacheDocument document)
    {
        var values = new Dictionary<string, PriceCacheItem>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in document.Items)
        {
            if (string.IsNullOrWhiteSpace(item.NormalizedName))
            {
                continue;
            }

            values[item.NormalizedName] = item;
            names.TryAdd(item.NormalizedName, item.Name);
        }

        return new PoeNinjaPrices(
            values,
            names.Values.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            document);
    }

    private static bool TryLoadFromDisk(string cachePath, out PoeNinjaPrices prices)
    {
        prices = null!;
        if (!File.Exists(cachePath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(cachePath);
            var document = JsonSerializer.Deserialize<PriceCacheDocument>(stream, JsonOptions);
            if (document is null)
            {
                return false;
            }

            prices = FromDocument(document);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFresh(DateTimeOffset fetchedUtc)
    {
        return DateTimeOffset.UtcNow - fetchedUtc < CacheLifetime;
    }

    private static string CachePath => AppPaths.ConfigFile("poe-ninja-price-cache.json");

    private static string HumanizeSlug(string value)
    {
        var words = Regex.Split(value.Trim(), @"[-_]+")
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .Select(word => word.All(char.IsDigit)
                ? word
                : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant());
        return string.Join(" ", words);
    }

    private static int EditDistance(string a, string b)
    {
        var costs = new int[b.Length + 1];
        for (var j = 0; j < costs.Length; j++)
        {
            costs[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            costs[0] = i;
            var northwest = i - 1;
            for (var j = 1; j <= b.Length; j++)
            {
                var deletion = costs[j] + 1;
                var insertion = costs[j - 1] + 1;
                var substitution = northwest + (a[i - 1] == b[j - 1] ? 0 : 1);
                northwest = costs[j];
                costs[j] = Math.Min(Math.Min(deletion, insertion), substitution);
            }
        }

        return costs[b.Length];
    }

    private static readonly Dictionary<string, string[]> IdAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gcp"] = ["Gemcutter's Prism"],
        ["bauble"] = ["Glassblower's Bauble"],
        ["etcher"] = ["Arcanist's Etcher"],
        ["aug"] = ["Orb of Augmentation"],
        ["alch"] = ["Orb of Alchemy"],
        ["transmute"] = ["Orb of Transmutation"],
        ["regal"] = ["Regal Orb"],
        ["chaos"] = ["Chaos Orb"],
        ["divine"] = ["Divine Orb"],
        ["exalted"] = ["Exalted Orb"],
        ["annul"] = ["Orb of Annulment"],
        ["artificers"] = ["Artificer's Orb"],
        ["chance"] = ["Orb of Chance"],
        ["mirror"] = ["Mirror of Kalandra"],
        ["scrap"] = ["Armourer's Scrap"],
        ["vaal"] = ["Vaal Orb"],
        ["whetstone"] = ["Blacksmith's Whetstone"],
        ["wisdom"] = ["Scroll of Wisdom"],
        ["lesser-jewellers-orb"] = ["Lesser Jeweller's Orb"],
        ["greater-jewellers-orb"] = ["Greater Jeweller's Orb"],
        ["perfect-jewellers-orb"] = ["Perfect Jeweller's Orb"],
        ["uncut-spirit-gem-10"] = ["Uncut Spirit Gem (Level 10)"],
        ["uncut-spirit-gem-11"] = ["Uncut Spirit Gem (Level 11)"],
        ["uncut-spirit-gem-12"] = ["Uncut Spirit Gem (Level 12)"],
        ["uncut-spirit-gem-13"] = ["Uncut Spirit Gem (Level 13)"],
        ["uncut-spirit-gem-14"] = ["Uncut Spirit Gem (Level 14)"],
        ["uncut-spirit-gem-15"] = ["Uncut Spirit Gem (Level 15)"],
        ["uncut-spirit-gem-16"] = ["Uncut Spirit Gem (Level 16)"],
        ["uncut-spirit-gem-17"] = ["Uncut Spirit Gem (Level 17)"],
        ["uncut-spirit-gem-18"] = ["Uncut Spirit Gem (Level 18)"],
        ["uncut-spirit-gem-19"] = ["Uncut Spirit Gem (Level 19)"],
        ["uncut-spirit-gem-20"] = ["Uncut Spirit Gem (Level 20)"],
        ["uncut-spirit-gem-level-10"] = ["Uncut Spirit Gem (Level 10)"],
        ["uncut-spirit-gem-level-11"] = ["Uncut Spirit Gem (Level 11)"],
        ["uncut-spirit-gem-level-12"] = ["Uncut Spirit Gem (Level 12)"],
        ["uncut-spirit-gem-level-13"] = ["Uncut Spirit Gem (Level 13)"],
        ["uncut-spirit-gem-level-14"] = ["Uncut Spirit Gem (Level 14)"],
        ["uncut-spirit-gem-level-15"] = ["Uncut Spirit Gem (Level 15)"],
        ["uncut-spirit-gem-level-16"] = ["Uncut Spirit Gem (Level 16)"],
        ["uncut-spirit-gem-level-17"] = ["Uncut Spirit Gem (Level 17)"],
        ["uncut-spirit-gem-level-18"] = ["Uncut Spirit Gem (Level 18)"],
        ["uncut-spirit-gem-level-19"] = ["Uncut Spirit Gem (Level 19)"],
        ["uncut-spirit-gem-level-20"] = ["Uncut Spirit Gem (Level 20)"]
    };
}

internal sealed record MarketValue(
    decimal Exalts,
    decimal Divines,
    PriceSource Source = PriceSource.PoeNinja,
    string? SourceCategory = null,
    string? SourceApiId = null,
    int? SourceQuantity = null,
    DateTimeOffset? SourceLogTime = null,
    string? SourceNote = null)
{
    public string SourceLabel => Source switch
    {
        PriceSource.Scout => "Scout",
        PriceSource.PoeNinjaFallback => "poe.ninja fallback",
        _ => "poe.ninja"
    };
}

internal enum PriceSource
{
    PoeNinja,
    Scout,
    PoeNinjaFallback
}

internal sealed record PoeNinjaPriceLookup(
    string Name,
    string NormalizedName,
    string Category,
    decimal Exalts,
    decimal Divines);

internal sealed record PriceCacheSummary(
    DateTimeOffset FetchedUtc,
    string League,
    int ItemCount,
    IReadOnlyList<string> FetchedCategories,
    IReadOnlyList<string> FailedCategories);

internal sealed record PriceLookupDiagnostic(
    string ItemName,
    string NormalizedName,
    IReadOnlyList<string> ExpectedCategories,
    IReadOnlyList<string> FetchedCategories,
    IReadOnlyList<string> FailedCategories,
    bool ExpectedCategoryFetched,
    IReadOnlyList<string> CloseMatches)
{
    public string ToDebugString()
    {
        var expected = ExpectedCategories.Count == 0 ? "(any)" : string.Join(", ", ExpectedCategories);
        var fetchedState = ExpectedCategoryFetched ? "expected category fetched" : "expected category not fetched";
        var close = CloseMatches.Count == 0 ? "none" : string.Join("; ", CloseMatches);
        var failed = FailedCategories.Count == 0 ? "none" : string.Join("; ", FailedCategories);
        return $"unpriced '{ItemName}' normalized '{NormalizedName}'; expected categories: {expected}; {fetchedState}; close matches: {close}; failed categories: {failed}";
    }
}

internal sealed record PriceCacheDocument(
    DateTimeOffset FetchedUtc,
    string League,
    IReadOnlyList<PriceCacheCategory> Categories,
    IReadOnlyList<string> FailedCategories,
    IReadOnlyList<PriceCacheItem> Items);

internal sealed record PriceCacheCategory(string Type, int LineCount);

internal sealed record PriceCacheItem(
    string Name,
    string NormalizedName,
    string Category,
    decimal Exalts,
    decimal Divines);

internal sealed record MarketValueConverter(string Primary, decimal ExaltedRate)
{
    public static MarketValueConverter From(JsonElement root)
    {
        var primary = "divine";
        var exaltedRate = 1m;
        if (root.TryGetProperty("core", out var core) && core.ValueKind == JsonValueKind.Object)
        {
            if (core.TryGetProperty("primary", out var primaryElement) && primaryElement.ValueKind == JsonValueKind.String)
            {
                primary = primaryElement.GetString() ?? primary;
            }

            if (core.TryGetProperty("rates", out var rates) &&
                rates.ValueKind == JsonValueKind.Object &&
                rates.TryGetProperty("exalted", out var exaltedElement) &&
                exaltedElement.TryGetDecimal(out var parsedRate) &&
                parsedRate > 0)
            {
                exaltedRate = parsedRate;
            }
        }

        return new MarketValueConverter(primary, exaltedRate);
    }

    public MarketValue ToMarketValue(decimal primaryValue)
    {
        if (Primary.Equals("divine", StringComparison.OrdinalIgnoreCase))
        {
            return new MarketValue(primaryValue * ExaltedRate, primaryValue);
        }

        if (Primary.Equals("exalted", StringComparison.OrdinalIgnoreCase))
        {
            return new MarketValue(primaryValue, ExaltedRate > 0 ? primaryValue / ExaltedRate : 0m);
        }

        return new MarketValue(primaryValue, 0m);
    }
}
