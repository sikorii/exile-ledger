using System.Globalization;

namespace Poe2PriceChecker;

internal static class PriceSourceComparisonReport
{
    private const decimal LargeDivergenceThresholdPercent = 50m;

    public static async Task<PriceSourceComparisonReportResult> WriteAsync(
        string itemListPath,
        string debugDirectory,
        CancellationToken cancellationToken)
    {
        var itemNames = File.ReadAllLines(itemListPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

        Directory.CreateDirectory(debugDirectory);

        var poeNinja = await PoeNinjaPrices.FetchAsync(cancellationToken).ConfigureAwait(false);
        var scout = await Poe2ScoutPrices.FetchAsync(cancellationToken).ConfigureAwait(false);

        var rows = new List<PriceSourceComparisonRow>();
        foreach (var itemName in itemNames)
        {
            var poeNinjaLookup = poeNinja.TryGetPriceLookup(itemName);
            var scoutLookup = await scout.TryGetPriceLookupAsync(itemName, cancellationToken).ConfigureAwait(false);
            rows.Add(BuildRow(itemName, poeNinjaLookup, scoutLookup));
        }

        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var csvPath = Path.Combine(debugDirectory, $"price-source-compare-{timestamp}.csv");
        var textPath = Path.Combine(debugDirectory, $"price-source-compare-{timestamp}.txt");

        File.WriteAllLines(csvPath, BuildCsvLines(rows));
        File.WriteAllLines(textPath, BuildTextLines(itemListPath, rows));

        return new PriceSourceComparisonReportResult(csvPath, textPath, rows);
    }

    private static PriceSourceComparisonRow BuildRow(
        string itemName,
        PoeNinjaPriceLookup? poeNinja,
        ScoutPriceLookup? scout)
    {
        var warnings = new List<string>();
        var notes = new List<string>();
        var normalizedName = PoeNinjaPrices.Normalize(itemName);
        var poeNinjaExalts = poeNinja?.Exalts;
        var scoutExalts = scout?.Exalts;

        if (poeNinja is null)
        {
            warnings.Add("missing from poe.ninja");
        }
        else
        {
            notes.Add($"poe.ninja matched {poeNinja.Name}");
            if (poeNinja.Exalts <= 0)
            {
                warnings.Add("suspicious poe.ninja price");
            }
        }

        if (scout is null)
        {
            warnings.Add("missing from Scout");
        }
        else
        {
            notes.Add($"Scout matched {scout.Name}");
            notes.Add(scout.Note);
            if (scout.Exalts is null or <= 0)
            {
                warnings.Add("suspicious Scout price");
            }
        }

        var divergence = ComputeDivergencePercent(poeNinjaExalts, scoutExalts);
        if (divergence is > LargeDivergenceThresholdPercent)
        {
            warnings.Add("large divergence > 50%");
        }

        return new PriceSourceComparisonRow(
            itemName,
            normalizedName,
            poeNinjaExalts,
            poeNinja?.Category,
            scoutExalts,
            scout?.Quantity,
            scout?.LogTime,
            scout?.CategoryApiId,
            scout?.ApiId,
            divergence,
            string.Join("; ", warnings.Distinct(StringComparer.OrdinalIgnoreCase)),
            string.Empty,
            string.Join("; ", notes.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private static decimal? ComputeDivergencePercent(decimal? poeNinjaExalts, decimal? scoutExalts)
    {
        if (poeNinjaExalts is not > 0 || scoutExalts is not > 0)
        {
            return null;
        }

        return Math.Abs(scoutExalts.Value - poeNinjaExalts.Value) / poeNinjaExalts.Value * 100m;
    }

    private static IEnumerable<string> BuildCsvLines(IReadOnlyList<PriceSourceComparisonRow> rows)
    {
        yield return "ItemName,NormalizedName,PoeNinjaExPerItem,PoeNinjaCategory,ScoutExPerItem,ScoutQuantity,ScoutLogTime,ScoutCategory,ScoutApiId,DivergencePct,Warning,ManualInGameExPerItem,Notes";
        foreach (var row in rows)
        {
            yield return string.Join(
                ",",
                Escape(row.ItemName),
                Escape(row.NormalizedName),
                FormatDecimal(row.PoeNinjaExPerItem),
                Escape(row.PoeNinjaCategory ?? string.Empty),
                FormatDecimal(row.ScoutExPerItem),
                row.ScoutQuantity?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.ScoutLogTime?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                Escape(row.ScoutCategory ?? string.Empty),
                Escape(row.ScoutApiId ?? string.Empty),
                FormatDecimal(row.DivergencePct),
                Escape(row.Warning),
                Escape(row.ManualInGameExPerItem),
                Escape(row.Notes));
        }
    }

    private static IEnumerable<string> BuildTextLines(string itemListPath, IReadOnlyList<PriceSourceComparisonRow> rows)
    {
        yield return "Price Source Comparison";
        yield return $"Generated: {DateTimeOffset.Now:O}";
        yield return $"Input: {itemListPath}";
        yield return "Values are exalts per 1 item.";
        yield return "Scout value uses displayed/current fields, not raw item history or daily highs.";
        yield return string.Empty;

        foreach (var row in rows)
        {
            yield return row.ItemName;
            yield return $"  Normalized: {row.NormalizedName}";
            yield return $"  poe.ninja: {FormatTextDecimal(row.PoeNinjaExPerItem)} ex" +
                (string.IsNullOrWhiteSpace(row.PoeNinjaCategory) ? string.Empty : $" [{row.PoeNinjaCategory}]");
            yield return $"  Scout: {FormatTextDecimal(row.ScoutExPerItem)} ex" +
                (string.IsNullOrWhiteSpace(row.ScoutCategory) ? string.Empty : $" [{row.ScoutCategory}/{row.ScoutApiId}]");
            yield return $"  Scout quantity: {row.ScoutQuantity?.ToString(CultureInfo.InvariantCulture) ?? "(missing)"}";
            yield return $"  Scout log time: {row.ScoutLogTime?.ToString("O", CultureInfo.InvariantCulture) ?? "(missing)"}";
            yield return $"  Divergence: {FormatTextDecimal(row.DivergencePct)}%";
            yield return $"  Warning: {(string.IsNullOrWhiteSpace(row.Warning) ? "(none)" : row.Warning)}";
            yield return $"  Manual in-game ex/item: (blank)";
            yield return $"  Notes: {(string.IsNullOrWhiteSpace(row.Notes) ? "(none)" : row.Notes)}";
            yield return string.Empty;
        }
    }

    private static string FormatDecimal(decimal? value)
    {
        return value?.ToString("0.##########", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string FormatTextDecimal(decimal? value)
    {
        return value?.ToString("0.##########", CultureInfo.InvariantCulture) ?? "(missing)";
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

internal sealed record PriceSourceComparisonReportResult(
    string CsvPath,
    string TextPath,
    IReadOnlyList<PriceSourceComparisonRow> Rows);

internal sealed record PriceSourceComparisonRow(
    string ItemName,
    string NormalizedName,
    decimal? PoeNinjaExPerItem,
    string? PoeNinjaCategory,
    decimal? ScoutExPerItem,
    int? ScoutQuantity,
    DateTimeOffset? ScoutLogTime,
    string? ScoutCategory,
    string? ScoutApiId,
    decimal? DivergencePct,
    string Warning,
    string ManualInGameExPerItem,
    string Notes);
