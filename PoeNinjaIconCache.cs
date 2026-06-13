using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Poe2PriceChecker;

internal sealed class PoeNinjaIconCache
{
    private const string League = "Runes of Aldur";
    private const string PoeNinjaBaseUrl = "https://poe.ninja";
    private const string PoeCdnBaseUrl = "https://web.poecdn.com";
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
    private static readonly string[] StashItemTypes = ["UniqueWeapons", "UniqueArmours", "UniqueAccessories"];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _indexPath;
    private readonly string _iconDirectory;

    public PoeNinjaIconCache(string indexPath, string iconDirectory)
    {
        _indexPath = indexPath;
        _iconDirectory = iconDirectory;
    }

    public async Task<PoeNinjaIconIndex> BuildAsync(bool forceDownload, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_indexPath)!);
        Directory.CreateDirectory(_iconDirectory);

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("POE2-Price-Checker/0.1");

        var entriesByKey = new Dictionary<string, PoeNinjaIconEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in ExchangeTypes)
        {
            await AddExchangeIconsAsync(client, type, entriesByKey, cancellationToken).ConfigureAwait(false);
        }

        foreach (var type in StashItemTypes)
        {
            await AddStashItemIconsAsync(client, type, entriesByKey, cancellationToken).ConfigureAwait(false);
        }

        var downloaded = 0;
        var failed = 0;
        var entries = new List<PoeNinjaIconEntry>();
        foreach (var entry in entriesByKey.Values.OrderBy(entry => entry.Type).ThenBy(entry => entry.ItemName))
        {
            var localPath = string.IsNullOrWhiteSpace(entry.LocalPath)
                ? BuildLocalPath(entry)
                : entry.LocalPath;
            var finalEntry = entry with { LocalPath = localPath };

            if (forceDownload || !File.Exists(localPath))
            {
                if (await TryDownloadIconAsync(client, finalEntry.SourceUrl, localPath, cancellationToken).ConfigureAwait(false))
                {
                    downloaded++;
                }
                else
                {
                    failed++;
                }
            }

            entries.Add(finalEntry);
        }

        var index = new PoeNinjaIconIndex(
            DateTimeOffset.UtcNow,
            League,
            entries.Count,
            downloaded,
            failed,
            entries);

        await File.WriteAllTextAsync(
            _indexPath,
            JsonSerializer.Serialize(index, JsonOptions),
            cancellationToken).ConfigureAwait(false);

        return index;
    }

    public async Task<PoeNinjaIconIndex?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_indexPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_indexPath);
        return await JsonSerializer.DeserializeAsync<PoeNinjaIconIndex>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<PoeNinjaIconIndex> LoadOrBuildAsync(CancellationToken cancellationToken)
    {
        return await LoadAsync(cancellationToken).ConfigureAwait(false) ??
            await BuildAsync(forceDownload: false, cancellationToken).ConfigureAwait(false);
    }

    public static PoeNinjaIconCache CreateDefault()
    {
        return new PoeNinjaIconCache(
            AppPaths.ConfigFile("poe-ninja-icons.json"),
            Path.Combine(AppPaths.CacheDirectory, "icons"));
    }

    private static async Task AddExchangeIconsAsync(
        HttpClient client,
        string type,
        Dictionary<string, PoeNinjaIconEntry> entriesByKey,
        CancellationToken cancellationToken)
    {
        var uri = $"{PoeNinjaBaseUrl}/poe2/api/economy/exchange/current/overview?league={Uri.EscapeDataString(League)}&type={Uri.EscapeDataString(type)}";
        using var response = await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.TryGetProperty("items", out var items) &&
            items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                AddIconEntry(type, item, "image", entriesByKey);
            }
        }

        if (!document.RootElement.TryGetProperty("core", out var core) ||
            !core.TryGetProperty("items", out var coreItems) ||
            coreItems.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in coreItems.EnumerateArray())
        {
            AddIconEntry(type, item, "image", entriesByKey);
        }
    }

    private static async Task AddStashItemIconsAsync(
        HttpClient client,
        string type,
        Dictionary<string, PoeNinjaIconEntry> entriesByKey,
        CancellationToken cancellationToken)
    {
        var uri = $"{PoeNinjaBaseUrl}/poe2/api/economy/stash/current/item/overview?league={Uri.EscapeDataString(League)}&type={Uri.EscapeDataString(type)}";
        using var response = await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var line in lines.EnumerateArray())
        {
            AddIconEntry(type, line, "icon", entriesByKey);
        }
    }

    private static void AddIconEntry(
        string type,
        JsonElement item,
        string iconPropertyName,
        Dictionary<string, PoeNinjaIconEntry> entriesByKey)
    {
        if (!TryGetString(item, "name", out var name) && !TryGetString(item, "itemId", out name))
        {
            return;
        }

        if (!TryGetString(item, iconPropertyName, out var iconUrl))
        {
            return;
        }

        var id = TryGetString(item, "id", out var parsedId)
            ? parsedId
            : TryGetString(item, "detailsId", out var detailsId)
                ? detailsId
                : name;
        var category = TryGetString(item, "category", out var parsedCategory) ? parsedCategory : type;
        var details = TryGetString(item, "detailsId", out var parsedDetails) ? parsedDetails : null;
        var sourceUrl = NormalizeIconUrl(iconUrl);
        var key = $"{type}|{id}|{name}|{sourceUrl}";

        entriesByKey[key] = new PoeNinjaIconEntry(
            type,
            id,
            name,
            category,
            details,
            sourceUrl,
            string.Empty);
    }

    private static async Task<bool> TryDownloadIconAsync(
        HttpClient client,
        string sourceUrl,
        string localPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await client.GetByteArrayAsync(sourceUrl, cancellationToken).ConfigureAwait(false);
            var directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = Path.Combine(directory ?? AppContext.BaseDirectory, $"{Path.GetFileName(localPath)}.{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);

            using (CurrencyScanner.LoadBitmapWithoutFileLock(tempPath))
            {
                // Validate that the downloaded payload is an image before it becomes part of the cache.
            }

            File.Move(tempPath, localPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string BuildLocalPath(PoeNinjaIconEntry entry)
    {
        var extension = Path.GetExtension(new Uri(entry.SourceUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
        {
            extension = ".png";
        }

        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(entry.SourceUrl)))[..12].ToLowerInvariant();
        var fileName = $"{Slug(entry.Type)}-{Slug(entry.Id)}-{hash}{extension}";
        return Path.Combine(_iconDirectory, fileName);
    }

    private static string NormalizeIconUrl(string iconUrl)
    {
        if (Uri.TryCreate(iconUrl, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        if (iconUrl.StartsWith("/gen/image/", StringComparison.OrdinalIgnoreCase))
        {
            return PoeCdnBaseUrl + iconUrl;
        }

        return PoeNinjaBaseUrl + (iconUrl.StartsWith('/') ? iconUrl : "/" + iconUrl);
    }

    private static string Slug(string value)
    {
        var slug = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "item" : slug;
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
            var text = element.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                value = text;
                return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Number)
        {
            value = element.GetRawText();
            return true;
        }

        return false;
    }
}

internal sealed class PoeNinjaIconMatcher
{
    private readonly IReadOnlyList<CachedIconSignature> _webSignatures;
    private readonly IReadOnlyList<CachedIconSignature> _localSignatures;

    private PoeNinjaIconMatcher(IReadOnlyList<CachedIconSignature> webSignatures, IReadOnlyList<CachedIconSignature> localSignatures)
    {
        _webSignatures = webSignatures;
        _localSignatures = localSignatures;
    }

    public static PoeNinjaIconMatcher FromIndex(PoeNinjaIconIndex index)
    {
        return FromIndex(index, LocalIconTemplateStore.CreateDefault());
    }

    public static PoeNinjaIconMatcher FromIndex(PoeNinjaIconIndex index, LocalIconTemplateStore templateStore)
    {
        var signatures = new List<CachedIconSignature>();
        foreach (var entry in index.Items)
        {
            if (!File.Exists(entry.LocalPath))
            {
                continue;
            }

            try
            {
                using var icon = CurrencyScanner.LoadBitmapWithoutFileLock(entry.LocalPath);
                using var prepared = IconCropPreprocessor.PrepareReferenceIcon(icon);
                signatures.Add(CachedIconSignature.FromPoeNinja(entry, IconMatchSignature.FromPrepared(prepared)));
            }
            catch
            {
                // A bad cache entry should not block matching against the rest of the cache.
            }
        }

        var localSignatures = new List<CachedIconSignature>();
        foreach (var entry in templateStore.Load())
        {
            try
            {
                using var template = CurrencyScanner.LoadBitmapWithoutFileLock(entry.TemplatePath);
                using var prepared = IconCropPreprocessor.PrepareRawSlotCrop(template);
                localSignatures.Add(CachedIconSignature.FromLocalTemplate(entry, IconMatchSignature.FromPrepared(prepared)));
            }
            catch
            {
                // User-generated templates are advisory; ignore unreadable files.
            }
        }

        return new PoeNinjaIconMatcher(signatures, localSignatures);
    }

    public IReadOnlyList<PoeNinjaIconMatch> MatchSlot(
        Bitmap screenshot,
        Rectangle slotBounds,
        int maxResults = 5,
        IReadOnlySet<string>? allowedTypes = null)
    {
        var context = new IconMatchContext("Unknown", allowedTypes);
        return MatchSlot(screenshot, slotBounds, maxResults, context);
    }

    public IReadOnlyList<PoeNinjaIconMatch> MatchSlot(
        Bitmap stashCrop,
        Rectangle slotBounds,
        int maxResults,
        IconMatchContext context)
    {
        using var prepared = IconCropPreprocessor.PrepareSlotIcon(stashCrop, slotBounds);
        return MatchPrepared(prepared, maxResults, context);
    }

    public IReadOnlyList<PoeNinjaIconMatch> MatchCrop(
        Bitmap slotCrop,
        int maxResults = 5,
        IReadOnlySet<string>? allowedTypes = null)
    {
        using var prepared = IconCropPreprocessor.PrepareRawSlotCrop(slotCrop);
        return MatchPrepared(prepared, maxResults, new IconMatchContext("Unknown", allowedTypes));
    }

    public IconMatchDebugResult WriteDebugForSlot(
        string debugDirectory,
        string mode,
        int slotIndex,
        Bitmap stashCrop,
        Rectangle slotBounds,
        IconMatchContext context)
    {
        var outputDirectory = Path.Combine(debugDirectory, "icon-match", Sanitize(mode), $"slot-{slotIndex:000}");
        Directory.CreateDirectory(outputDirectory);

        using var prepared = IconCropPreprocessor.PrepareSlotIcon(stashCrop, slotBounds);
        var cleanedPath = Path.Combine(outputDirectory, "cleaned-slot.png");
        CurrencyScanner.SaveBitmap(prepared, cleanedPath);

        var matches = MatchPrepared(prepared, 5, context);
        var lines = new List<string>
        {
            $"Mode: {mode}",
            $"Slot: {slotIndex}",
            $"Tab: {context.TabKey}",
            $"Slot section: {context.SlotSection ?? string.Empty}",
            $"Allowed types: {(context.AllowedTypes is null ? "(all)" : string.Join(", ", context.AllowedTypes))}",
            $"Slot bounds: {slotBounds.X},{slotBounds.Y},{slotBounds.Width},{slotBounds.Height}",
            $"Icon-only crop bounds: {IconCropPreprocessor.BuildIconOnlyBounds(slotBounds).X},{IconCropPreprocessor.BuildIconOnlyBounds(slotBounds).Y},{IconCropPreprocessor.BuildIconOnlyBounds(slotBounds).Width},{IconCropPreprocessor.BuildIconOnlyBounds(slotBounds).Height}",
            $"Cleaned slot crop: {cleanedPath}",
            string.Empty,
            "rank,confidence,gap,source,hash,histogram,edge,pixel,type,item,path,reason"
        };

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            lines.Add(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{i + 1},{match.Confidence:0.000},{match.SecondBestGap:0.000},{match.SourceKind},{match.HashScore:0.000},{match.HistogramScore:0.000},{match.EdgeScore:0.000},{match.PixelScore:0.000},{match.Type},{match.ItemName},{match.LocalPath},{match.Reason}"));

            if (File.Exists(match.LocalPath))
            {
                var extension = Path.GetExtension(match.LocalPath);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = ".png";
                }

                var candidatePath = Path.Combine(outputDirectory, $"candidate-{i + 1:00}-{Sanitize(match.ItemName)}{extension}");
                File.Copy(match.LocalPath, candidatePath, overwrite: true);
            }
        }

        var reportPath = Path.Combine(outputDirectory, "scores.txt");
        File.WriteAllLines(reportPath, lines);
        return new IconMatchDebugResult(cleanedPath, reportPath, matches);
    }

    private IReadOnlyList<PoeNinjaIconMatch> MatchPrepared(
        Bitmap preparedSlot,
        int maxResults,
        IconMatchContext context)
    {
        if (_webSignatures.Count == 0 && _localSignatures.Count == 0)
        {
            return [];
        }

        var input = IconMatchSignature.FromPrepared(preparedSlot);
        var rawMatches = _localSignatures
            .Where(signature => signature.MatchesContext(context))
            .Concat(_webSignatures.Where(signature => signature.MatchesContext(context)))
            .Select(signature => BuildMatch(input, signature))
            .GroupBy(match => match.ItemName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(match => match.SourceKind == "local-template")
                .ThenByDescending(match => match.Confidence)
                .First())
            .OrderByDescending(match => match.SourceKind == "local-template")
            .ThenByDescending(match => match.Confidence)
            .Take(Math.Max(1, maxResults))
            .ToArray();

        if (rawMatches.Length == 0)
        {
            return rawMatches;
        }

        var best = rawMatches[0];
        var secondConfidence = rawMatches.Length > 1 ? rawMatches[1].Confidence : 0;
        return rawMatches
            .Select((match, index) => match with
            {
                SecondBestGap = index == 0
                    ? Math.Max(0, best.Confidence - secondConfidence)
                    : Math.Max(0, best.Confidence - match.Confidence)
            })
            .ToArray();
    }

    private static PoeNinjaIconMatch BuildMatch(IconMatchSignature input, CachedIconSignature signature)
    {
        var scores = input.Compare(signature.Signature);
        var localBonus = signature.SourceKind == "local-template" ? 0.04 : 0;
        var confidence = Math.Clamp(scores.CombinedConfidence + localBonus, 0, 1);
        var reason =
            $"{signature.SourceKind}; combined={confidence:0.000}; " +
            $"hash={scores.HashSimilarity:0.000}; hist={scores.HistogramSimilarity:0.000}; " +
            $"edge={scores.EdgeSimilarity:0.000}; pixel={scores.PixelSimilarity:0.000}";

        return new PoeNinjaIconMatch(
            signature.ItemName,
            signature.Type,
            signature.Id,
            signature.LocalPath,
            confidence,
            signature.SourceKind,
            scores.HashSimilarity,
            scores.HistogramSimilarity,
            scores.EdgeSimilarity,
            scores.PixelSimilarity,
            0,
            reason);
    }

    private static string Sanitize(string value)
    {
        var safe = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "item" : safe;
    }

    private sealed record CachedIconSignature(
        string ItemName,
        string Type,
        string Id,
        string LocalPath,
        string SourceKind,
        string? TabKey,
        string? SlotSection,
        IconMatchSignature Signature)
    {
        public static CachedIconSignature FromPoeNinja(PoeNinjaIconEntry entry, IconMatchSignature signature)
        {
            return new CachedIconSignature(
                entry.ItemName,
                entry.Type,
                entry.Id,
                entry.LocalPath,
                "poe.ninja",
                null,
                null,
                signature);
        }

        public static CachedIconSignature FromLocalTemplate(LocalIconTemplateEntry entry, IconMatchSignature signature)
        {
            return new CachedIconSignature(
                entry.ItemName,
                entry.TabKey,
                $"{entry.TabKey}:{entry.SlotIndex}:{entry.ItemName}",
                entry.TemplatePath,
                "local-template",
                entry.TabKey,
                entry.SlotSection,
                signature);
        }

        public bool MatchesContext(IconMatchContext context)
        {
            if (SourceKind == "local-template")
            {
                if (!string.IsNullOrWhiteSpace(context.TabKey) &&
                    !string.IsNullOrWhiteSpace(TabKey) &&
                    !TabKey.Equals(context.TabKey, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return string.IsNullOrWhiteSpace(context.SlotSection) ||
                    string.IsNullOrWhiteSpace(SlotSection) ||
                    SlotSection.Equals(context.SlotSection, StringComparison.OrdinalIgnoreCase);
            }

            return context.AllowedTypes is null || context.AllowedTypes.Contains(Type);
        }
    }
}

internal sealed record PoeNinjaIconIndex(
    DateTimeOffset BuiltUtc,
    string League,
    int ItemCount,
    int DownloadedCount,
    int FailedDownloadCount,
    IReadOnlyList<PoeNinjaIconEntry> Items);

internal sealed record PoeNinjaIconEntry(
    string Type,
    string Id,
    string ItemName,
    string Category,
    string? DetailsId,
    string SourceUrl,
    string LocalPath);

internal sealed record PoeNinjaIconMatch(
    string ItemName,
    string Type,
    string Id,
    string LocalPath,
    double Confidence,
    string SourceKind = "poe.ninja",
    double HashScore = 0,
    double HistogramScore = 0,
    double EdgeScore = 0,
    double PixelScore = 0,
    double SecondBestGap = 0,
    string Reason = "")
{
    public bool ShouldAutoAccept => Confidence >= 0.94 && SecondBestGap >= 0.08;
}

internal sealed record IconMatchDebugResult(
    string CleanedSlotPath,
    string ReportPath,
    IReadOnlyList<PoeNinjaIconMatch> Matches);
