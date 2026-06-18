namespace Poe2PriceChecker;

internal sealed class LiveMarketPrices
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    private static LiveMarketPrices? _memoryCache;
    private static DateTimeOffset _lastLiveFetchUtc = DateTimeOffset.MinValue;

    private readonly Poe2ScoutPrices? _scout;
    private readonly PoeNinjaPrices? _poeNinja;
    private readonly DateTimeOffset _fetchedUtc;
    private readonly string? _scoutFailure;
    private readonly string? _poeNinjaFailure;
    private readonly decimal? _divineExalts;

    private LiveMarketPrices(
        Poe2ScoutPrices? scout,
        PoeNinjaPrices? poeNinja,
        DateTimeOffset fetchedUtc,
        string? scoutFailure,
        string? poeNinjaFailure)
    {
        _scout = scout;
        _poeNinja = poeNinja;
        _fetchedUtc = fetchedUtc;
        _scoutFailure = scoutFailure;
        _poeNinjaFailure = poeNinjaFailure;
        var scoutDivineExalts = scout?.TryGetCurrentPriceLookup("Divine Orb")?.Exalts;
        var poeNinjaDivineExalts = poeNinja?.TryGetPriceLookup("Divine Orb")?.Exalts;
        var divineExalts = scoutDivineExalts is > 0m ? scoutDivineExalts : poeNinjaDivineExalts;
        _divineExalts = divineExalts is > 0m ? divineExalts : null;
    }

    public LivePriceCacheSummary CacheSummary => new(
        _fetchedUtc,
        _scout?.ItemCount ?? 0,
        _scout?.Categories ?? [],
        _scoutFailure,
        _poeNinja?.CacheSummary,
        _poeNinjaFailure);

    public static async Task<LiveMarketPrices> FetchAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        await RefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _memoryCache is not null && IsFresh(_memoryCache._fetchedUtc))
            {
                return _memoryCache;
            }

            if (forceRefresh &&
                _memoryCache is not null &&
                DateTimeOffset.UtcNow - _lastLiveFetchUtc < TimeSpan.FromSeconds(20))
            {
                return _memoryCache;
            }

            var scoutTask = FetchScoutSafelyAsync(cancellationToken);
            var poeNinjaTask = FetchPoeNinjaSafelyAsync(cancellationToken, forceRefresh);

            await Task.WhenAll(scoutTask, poeNinjaTask).ConfigureAwait(false);

            var scoutResult = await scoutTask.ConfigureAwait(false);
            var poeNinjaResult = await poeNinjaTask.ConfigureAwait(false);
            if (scoutResult.Prices is null && poeNinjaResult.Prices is null)
            {
                throw new InvalidOperationException(
                    $"All price sources failed. Scout: {scoutResult.Failure ?? "unknown"}; poe.ninja: {poeNinjaResult.Failure ?? "unknown"}");
            }

            var live = new LiveMarketPrices(
                scoutResult.Prices,
                poeNinjaResult.Prices,
                DateTimeOffset.UtcNow,
                scoutResult.Failure,
                poeNinjaResult.Failure);
            _memoryCache = live;
            _lastLiveFetchUtc = DateTimeOffset.UtcNow;
            return live;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    public MarketValue? TryGetValue(string itemName, int quantity)
    {
        if (_scout is not null)
        {
            var scout = _scout.TryGetCurrentPriceLookup(itemName);
            if (scout?.Exalts is > 0m)
            {
                var exalts = scout.Exalts.Value * quantity;
                return new MarketValue(
                    exalts,
                    ToDivines(exalts),
                    PriceSource.Scout,
                    scout.CategoryApiId,
                    scout.ApiId,
                    scout.Quantity,
                    scout.LogTime,
                    scout.Note);
            }
        }

        if (_poeNinja is not null)
        {
            var value = _poeNinja.TryGetValue(itemName, quantity);
            var lookup = _poeNinja.TryGetPriceLookup(itemName);
            if (value is not null)
            {
                return value with
                {
                    Source = PriceSource.PoeNinjaFallback,
                    SourceCategory = lookup?.Category,
                    SourceNote = "poe.ninja fallback"
                };
            }
        }

        return null;
    }

    public LivePriceLookupDiagnostic DiagnoseMissing(string itemName, IReadOnlySet<string>? expectedCategories = null)
    {
        return new LivePriceLookupDiagnostic(
            itemName,
            PoeNinjaPrices.Normalize(itemName),
            _scout is null,
            _scoutFailure,
            _poeNinja?.DiagnoseMissing(itemName, expectedCategories),
            _poeNinjaFailure);
    }

    public string FormatSourceDebug(string itemName, int quantity, MarketValue? value)
    {
        if (value is null)
        {
            return $"price source: unavailable for {itemName}";
        }

        var unitExalts = quantity > 0 ? value.Exalts / quantity : value.Exalts;
        var details = new List<string>
        {
            $"price source: {value.SourceLabel}",
            $"unit {unitExalts:0.##########} ex"
        };

        if (!string.IsNullOrWhiteSpace(value.SourceCategory))
        {
            details.Add($"category {value.SourceCategory}");
        }

        if (!string.IsNullOrWhiteSpace(value.SourceApiId))
        {
            details.Add($"api {value.SourceApiId}");
        }

        if (value.SourceQuantity is not null)
        {
            details.Add($"qty {value.SourceQuantity.Value}");
        }

        if (value.SourceLogTime is not null)
        {
            details.Add($"log {value.SourceLogTime.Value:O}");
        }

        return string.Join("; ", details);
    }

    private decimal ToDivines(decimal exalts)
    {
        return _divineExalts is > 0m ? exalts / _divineExalts.Value : 0m;
    }

    private static async Task<(Poe2ScoutPrices? Prices, string? Failure)> FetchScoutSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await Poe2ScoutPrices.FetchAsync(cancellationToken).ConfigureAwait(false), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static async Task<(PoeNinjaPrices? Prices, string? Failure)> FetchPoeNinjaSafelyAsync(
        CancellationToken cancellationToken,
        bool forceRefresh)
    {
        try
        {
            return (await PoeNinjaPrices.FetchAsync(cancellationToken, forceRefresh).ConfigureAwait(false), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static bool IsFresh(DateTimeOffset fetchedUtc)
    {
        return DateTimeOffset.UtcNow - fetchedUtc < CacheLifetime;
    }
}

internal sealed record LivePriceCacheSummary(
    DateTimeOffset FetchedUtc,
    int ScoutItemCount,
    IReadOnlyList<string> ScoutCategories,
    string? ScoutFailure,
    PriceCacheSummary? PoeNinja,
    string? PoeNinjaFailure)
{
    public int ItemCount => ScoutItemCount + (PoeNinja?.ItemCount ?? 0);

    public IReadOnlyList<string> FetchedCategories =>
        ScoutCategories.Select(category => $"Scout:{category}")
            .Concat(PoeNinja?.FetchedCategories.Select(category => $"poe.ninja:{category}") ?? [])
            .ToArray();

    public IReadOnlyList<string> FailedCategories =>
        (ScoutFailure is null ? [] : new[] { $"Scout: {ScoutFailure}" })
        .Concat(PoeNinjaFailure is null ? [] : new[] { $"poe.ninja: {PoeNinjaFailure}" })
        .Concat(PoeNinja?.FailedCategories.Select(category => $"poe.ninja:{category}") ?? [])
        .ToArray();
}

internal sealed record LivePriceLookupDiagnostic(
    string ItemName,
    string NormalizedName,
    bool ScoutUnavailable,
    string? ScoutFailure,
    PriceLookupDiagnostic? PoeNinjaDiagnostic,
    string? PoeNinjaFailure)
{
    public string ToDebugString()
    {
        var parts = new List<string>
        {
            $"unpriced '{ItemName}' normalized '{NormalizedName}'",
            ScoutUnavailable
                ? $"Scout unavailable: {ScoutFailure ?? "unknown"}"
                : "Scout: no confident positive CurrentPrice match",
            PoeNinjaDiagnostic is null
                ? $"poe.ninja fallback unavailable: {PoeNinjaFailure ?? "not loaded"}"
                : $"poe.ninja fallback: {PoeNinjaDiagnostic.ToDebugString()}"
        };

        return string.Join("; ", parts);
    }
}
