using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Poe2PriceChecker;

internal static class CountCropReviewReport
{
    public static CountCropReviewReportResult Generate(string baseDirectory)
    {
        var outputDirectory = Path.Combine(baseDirectory, "review");
        Directory.CreateDirectory(outputDirectory);
        var reportPath = Path.Combine(outputDirectory, "count-crop-review.html");

        var scanRoots = BuildScanRoots(baseDirectory).ToArray();
        var items = scanRoots
            .SelectMany(root => LoadItems(root.Directory, root.Kind))
            .OrderByDescending(item => item.TimestampUtc ?? File.GetLastWriteTimeUtc(item.RawPath ?? item.CleanedPath ?? item.MetadataPath ?? string.Empty))
            .ThenBy(item => item.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ScanId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SlotIndex ?? int.MaxValue)
            .ToArray();

        var html = BuildHtml(reportPath, scanRoots, items);
        File.WriteAllText(reportPath, html, Encoding.UTF8);
        return new CountCropReviewReportResult(
            reportPath,
            scanRoots.Select(root => root.Directory).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            items.Length,
            items.Count(item => item.WarningFlags.Count > 0));
    }

    public static bool TryOpenInDefaultBrowser(string reportPath, out string error)
    {
        try
        {
            Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static IEnumerable<CountCropScanRoot> BuildScanRoots(string baseDirectory)
    {
        var bases = new List<string>
        {
            baseDirectory,
            Directory.GetCurrentDirectory()
        };

        var projectRoot = FindProjectRoot(baseDirectory);
        if (projectRoot is not null)
        {
            bases.Add(projectRoot);
            bases.Add(Path.Combine(projectRoot, "publish"));
        }

        foreach (var root in bases
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return new CountCropScanRoot(Path.Combine(root, "debug", "count-crops"), "debug");
            yield return new CountCropScanRoot(Path.Combine(root, "training", "count-crops", "labeled"), "labeled");
        }
    }

    private static string? FindProjectRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExileLedger.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static IEnumerable<CountCropReviewItem> LoadItems(string rootDirectory, string kind)
    {
        if (!Directory.Exists(rootDirectory))
        {
            yield break;
        }

        var seenPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawPath in Directory.EnumerateFiles(rootDirectory, "*-raw.png", SearchOption.AllDirectories))
        {
            var prefix = rawPath[..^"-raw.png".Length];
            seenPrefixes.Add(prefix);
            yield return BuildItem(rootDirectory, kind, prefix, rawPath, FindCleanedPath(prefix), $"{prefix}.json");
        }

        foreach (var cleanedPath in Directory.EnumerateFiles(rootDirectory, "*-cleaned.png", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(rootDirectory, "*-processed.png", SearchOption.AllDirectories)))
        {
            var suffix = cleanedPath.EndsWith("-cleaned.png", StringComparison.OrdinalIgnoreCase)
                ? "-cleaned.png"
                : "-processed.png";
            var prefix = cleanedPath[..^suffix.Length];
            if (seenPrefixes.Add(prefix))
            {
                yield return BuildItem(rootDirectory, kind, prefix, null, cleanedPath, $"{prefix}.json");
            }
        }
    }

    private static string? FindCleanedPath(string prefix)
    {
        var cleanedPath = $"{prefix}-cleaned.png";
        if (File.Exists(cleanedPath))
        {
            return cleanedPath;
        }

        var processedPath = $"{prefix}-processed.png";
        return File.Exists(processedPath) ? processedPath : null;
    }

    private static CountCropReviewItem BuildItem(
        string rootDirectory,
        string kind,
        string prefix,
        string? rawPath,
        string? cleanedPath,
        string metadataPath)
    {
        var metadata = File.Exists(metadataPath)
            ? TryReadMetadata(metadataPath)
            : null;
        var relativeParts = Path.GetRelativePath(rootDirectory, prefix)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(part => part.Length > 0)
            .ToArray();
        var fileName = Path.GetFileName(prefix);

        var profileName = metadata?.ProfileName ?? InferProfileName(kind, relativeParts, fileName);
        var scanId = metadata?.ScanId ?? InferScanId(kind, relativeParts);
        var slotIndex = metadata?.SlotIndex ?? InferInt(fileName, @"slot-(\d+)");
        var guessedCount = metadata?.GuessedCount ?? InferInt(fileName, @"guess-(\d+)");
        var correctedCount = metadata?.CorrectedCount ?? InferCorrectedCount(kind, relativeParts, fileName);
        var cropBounds = metadata?.CropBounds ?? string.Empty;
        var sourcePath = metadata?.SourceImagePath ?? string.Empty;
        var timestampUtc = metadata?.TimestampUtc ?? InferTimestamp(fileName);
        var countMethod = metadata?.CountMethod ?? string.Empty;

        var flags = AnalyzeQuality(rawPath, cleanedPath, File.Exists(metadataPath), guessedCount, correctedCount);
        return new CountCropReviewItem(
            kind,
            profileName,
            scanId,
            slotIndex,
            guessedCount,
            correctedCount,
            cropBounds,
            sourcePath,
            timestampUtc,
            countMethod,
            rawPath,
            cleanedPath,
            File.Exists(metadataPath) ? metadataPath : null,
            flags.Where(flag => !flag.IsInfo).Select(flag => flag.Text).ToArray(),
            flags.Where(flag => flag.IsInfo).Select(flag => flag.Text).ToArray());
    }

    private static CountCropMetadataView? TryReadMetadata(string metadataPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            var root = document.RootElement;
            return new CountCropMetadataView(
                GetString(root, "ProfileName"),
                GetInt(root, "SlotIndex"),
                GetInt(root, "GuessedCount"),
                GetInt(root, "CorrectedCount"),
                GetRectangle(root, "CropBounds"),
                GetString(root, "SourceImagePath"),
                GetDateTimeOffset(root, "TimestampUtc"),
                GetString(root, "ScanId"),
                GetString(root, "CountMethod"));
        }
        catch
        {
            return null;
        }
    }

    private static string InferProfileName(string kind, IReadOnlyList<string> relativeParts, string fileName)
    {
        if (kind == "debug" && relativeParts.Count >= 1)
        {
            return relativeParts[0];
        }

        var match = Regex.Match(fileName, @"^\d{8}-\d{9,}-(?<profile>.+?)-slot-\d+", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["profile"].Value : "unknown";
    }

    private static string InferScanId(string kind, IReadOnlyList<string> relativeParts)
    {
        return kind == "debug" && relativeParts.Count >= 2
            ? relativeParts[1]
            : string.Empty;
    }

    private static int? InferCorrectedCount(string kind, IReadOnlyList<string> relativeParts, string fileName)
    {
        var fromFile = InferInt(fileName, @"label-(\d+)");
        if (fromFile is not null)
        {
            return fromFile;
        }

        return kind == "labeled" && relativeParts.Count >= 1 && int.TryParse(relativeParts[0], out var label)
            ? label
            : null;
    }

    private static int? InferInt(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? InferTimestamp(string fileName)
    {
        var match = Regex.Match(fileName, @"^(?<date>\d{8})-(?<time>\d{6})(?<ms>\d{3})");
        if (!match.Success)
        {
            return null;
        }

        var text = $"{match.Groups["date"].Value}{match.Groups["time"].Value}{match.Groups["ms"].Value}";
        return DateTimeOffset.TryParseExact(
            text,
            "yyyyMMddHHmmssfff",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static IReadOnlyList<CountCropQualityFlag> AnalyzeQuality(
        string? rawPath,
        string? cleanedPath,
        bool hasMetadata,
        int? guessedCount,
        int? correctedCount)
    {
        var flags = new List<CountCropQualityFlag>();
        if (!hasMetadata)
        {
            flags.Add(new CountCropQualityFlag("missing metadata json", false));
        }

        if (correctedCount is not null && guessedCount is not null && correctedCount != guessedCount)
        {
            flags.Add(new CountCropQualityFlag("info: label/guess mismatch", true));
        }

        if (rawPath is null || !File.Exists(rawPath))
        {
            flags.Add(new CountCropQualityFlag("missing raw crop", false));
        }
        else
        {
            try
            {
                using var raw = CurrencyScanner.LoadBitmapWithoutFileLock(rawPath);
                if (raw.Height is < 30 or > 50 || raw.Width is < 36 or > 112)
                {
                    flags.Add(new CountCropQualityFlag($"raw dimensions unusual ({raw.Width}x{raw.Height})", false));
                }
            }
            catch
            {
                flags.Add(new CountCropQualityFlag("raw crop could not be read", false));
            }
        }

        if (cleanedPath is null || !File.Exists(cleanedPath))
        {
            flags.Add(new CountCropQualityFlag("missing cleaned crop", false));
            return flags;
        }

        try
        {
            using var cleaned = CurrencyScanner.LoadBitmapWithoutFileLock(cleanedPath);
            if (cleaned.Height is < 120 or > 190 || cleaned.Width is < 140 or > 450)
            {
                flags.Add(new CountCropQualityFlag($"cleaned dimensions unusual ({cleaned.Width}x{cleaned.Height})", false));
            }

            var stats = GetForegroundStats(cleaned);
            if (stats.ForegroundPixels < 16 || stats.ForegroundRatio < 0.0015)
            {
                flags.Add(new CountCropQualityFlag("likely blank cleaned crop", false));
            }
            else if (stats.ForegroundPixels < 70 || stats.ForegroundRatio < 0.004)
            {
                flags.Add(new CountCropQualityFlag("very low foreground pixel count", false));
            }

            if (stats.ForegroundRatio > 0.35)
            {
                flags.Add(new CountCropQualityFlag("too much foreground/noise", false));
            }

            if (stats.LeftEdge > 2)
            {
                flags.Add(new CountCropQualityFlag("content touches left edge", false));
            }

            if (stats.RightEdge > 2)
            {
                flags.Add(new CountCropQualityFlag("content touches right edge", false));
            }

            if (stats.TopEdge > 2)
            {
                flags.Add(new CountCropQualityFlag("content touches top edge", false));
            }

            if (stats.BottomEdge > 2)
            {
                flags.Add(new CountCropQualityFlag("content touches bottom edge", false));
            }

            if (stats.LeftEdge > 2 || stats.RightEdge > 2 || stats.TopEdge > 2 || stats.BottomEdge > 2)
            {
                flags.Add(new CountCropQualityFlag("crop may be clipped", false));
            }
        }
        catch
        {
            flags.Add(new CountCropQualityFlag("cleaned crop could not be read", false));
        }

        return flags;
    }

    private static CountCropForegroundStats GetForegroundStats(Bitmap image)
    {
        var foreground = 0;
        var left = 0;
        var right = 0;
        var top = 0;
        var bottom = 0;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var color = image.GetPixel(x, y);
                if (color.R >= 128 || color.G >= 128 || color.B >= 128)
                {
                    continue;
                }

                foreground++;
                if (x <= 1)
                {
                    left++;
                }

                if (x >= image.Width - 2)
                {
                    right++;
                }

                if (y <= 1)
                {
                    top++;
                }

                if (y >= image.Height - 2)
                {
                    bottom++;
                }
            }
        }

        return new CountCropForegroundStats(
            foreground,
            foreground / (double)Math.Max(1, image.Width * image.Height),
            left,
            right,
            top,
            bottom);
    }

    private static string BuildHtml(string reportPath, IReadOnlyList<CountCropScanRoot> scanRoots, IReadOnlyList<CountCropReviewItem> items)
    {
        var html = new StringBuilder();
        var suspectItems = items.Where(item => item.WarningFlags.Count > 0).ToArray();
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html><head><meta charset=\"utf-8\"><title>POE2 Count Crop Review</title>");
        html.AppendLine("<style>");
        html.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;background:#151515;color:#e8e8e8;margin:24px}");
        html.AppendLine("h1,h2,h3{margin:20px 0 10px} .muted{color:#a8a8a8} .section{margin-top:28px}");
        html.AppendLine(".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(420px,1fr));gap:14px}");
        html.AppendLine(".card{border:1px solid #3a3a3a;background:#202020;border-radius:6px;padding:12px}");
        html.AppendLine(".images{display:flex;gap:10px;align-items:flex-start}.imgbox{min-width:0}.imgbox img{background:white;border:1px solid #555;image-rendering:pixelated;max-width:190px;max-height:110px}");
        html.AppendLine(".label{font-size:12px;color:#bdbdbd;margin-bottom:4px}.meta{font-size:12px;line-height:1.45;word-break:break-word;margin-top:8px}.flags{margin-top:8px}");
        html.AppendLine(".flag{display:inline-block;background:#693018;color:#ffd7ba;border:1px solid #a05020;border-radius:4px;padding:2px 6px;margin:2px;font-size:12px}");
        html.AppendLine(".info{display:inline-block;background:#263852;color:#c9ddff;border:1px solid #496a9c;border-radius:4px;padding:2px 6px;margin:2px;font-size:12px}");
        html.AppendLine("a{color:#8fc7ff}.empty{padding:14px;border:1px dashed #555;color:#bbb}");
        html.AppendLine("</style></head><body>");
        html.AppendLine("<h1>POE2 Count Crop Review</h1>");
        html.AppendLine($"<div class=\"muted\">Generated {Html(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"))}</div>");
        html.AppendLine($"<p>{items.Count} crop pairs found. {suspectItems.Length} have warning flags. Flags are review hints only; they do not relabel or validate counts.</p>");
        html.AppendLine("<h2>Scanned Folders</h2><ul>");
        foreach (var root in scanRoots.Select(root => root.Directory).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            html.AppendLine($"<li>{Html(root)} {(Directory.Exists(root) ? string.Empty : "<span class=\"muted\">(missing)</span>")}</li>");
        }

        html.AppendLine("</ul>");
        html.AppendLine("<h2>Suspect Crops</h2>");
        AppendCards(html, reportPath, suspectItems.Take(200).ToArray());
        if (suspectItems.Length > 200)
        {
            html.AppendLine($"<p class=\"muted\">Showing first 200 of {suspectItems.Length} suspect crops.</p>");
        }

        html.AppendLine("<div class=\"section\"><h2>Labeled Training Samples</h2></div>");
        foreach (var group in items
            .Where(item => item.CorrectedCount is not null || item.Kind == "labeled")
            .GroupBy(item => item.CorrectedCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown")
            .OrderBy(group => int.TryParse(group.Key, out var value) ? value : int.MaxValue))
        {
            html.AppendLine($"<h3>Label {Html(group.Key)} ({group.Count()})</h3>");
            AppendCards(html, reportPath, group.ToArray());
        }

        html.AppendLine("<div class=\"section\"><h2>Debug / Unlabeled Crops</h2></div>");
        foreach (var profileGroup in items
            .Where(item => item.CorrectedCount is null && item.Kind != "labeled")
            .GroupBy(item => item.ProfileName)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            html.AppendLine($"<h3>{Html(profileGroup.Key)} ({profileGroup.Count()})</h3>");
            foreach (var scanGroup in profileGroup
                .GroupBy(item => string.IsNullOrWhiteSpace(item.ScanId) ? "unknown scan" : item.ScanId)
                .OrderByDescending(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                html.AppendLine($"<h4>{Html(scanGroup.Key)} ({scanGroup.Count()})</h4>");
                AppendCards(html, reportPath, scanGroup.ToArray());
            }
        }

        html.AppendLine("</body></html>");
        return html.ToString();
    }

    private static void AppendCards(StringBuilder html, string reportPath, IReadOnlyList<CountCropReviewItem> items)
    {
        if (items.Count == 0)
        {
            html.AppendLine("<div class=\"empty\">No crops in this section.</div>");
            return;
        }

        html.AppendLine("<div class=\"grid\">");
        foreach (var item in items)
        {
            html.AppendLine("<div class=\"card\">");
            html.AppendLine("<div class=\"images\">");
            AppendImage(html, reportPath, "Raw", item.RawPath);
            AppendImage(html, reportPath, "Cleaned", item.CleanedPath);
            html.AppendLine("</div>");
            html.AppendLine("<div class=\"flags\">");
            foreach (var flag in item.WarningFlags)
            {
                html.AppendLine($"<span class=\"flag\">{Html(flag)}</span>");
            }

            foreach (var flag in item.InfoFlags)
            {
                html.AppendLine($"<span class=\"info\">{Html(flag)}</span>");
            }

            html.AppendLine("</div>");
            html.AppendLine("<div class=\"meta\">");
            html.AppendLine($"<b>{Html(item.ProfileName)}</b> slot {Html(item.SlotIndex?.ToString() ?? "?")}<br>");
            html.AppendLine($"kind: {Html(item.Kind)} | scan: {Html(string.IsNullOrWhiteSpace(item.ScanId) ? "unknown" : item.ScanId)}<br>");
            html.AppendLine($"guess: {Html(item.GuessedCount?.ToString() ?? "unknown")} | label: {Html(item.CorrectedCount?.ToString() ?? "none")} | method: {Html(item.CountMethod)}<br>");
            html.AppendLine($"bounds: {Html(item.CropBounds)}<br>");
            html.AppendLine($"time: {Html(item.TimestampUtc?.ToString("O") ?? "unknown")}<br>");
            html.AppendLine($"source: {Html(item.SourcePath)}<br>");
            html.AppendLine($"metadata: {Html(item.MetadataPath ?? "missing")}");
            html.AppendLine("</div></div>");
        }

        html.AppendLine("</div>");
    }

    private static void AppendImage(StringBuilder html, string reportPath, string label, string? path)
    {
        html.AppendLine("<div class=\"imgbox\">");
        html.AppendLine($"<div class=\"label\">{Html(label)}</div>");
        if (path is not null && File.Exists(path))
        {
            html.AppendLine($"<a href=\"{HtmlAttr(RelativePath(reportPath, path))}\"><img src=\"{HtmlAttr(RelativePath(reportPath, path))}\" alt=\"{HtmlAttr(label)}\"></a>");
        }
        else
        {
            html.AppendLine("<div class=\"empty\">missing</div>");
        }

        html.AppendLine("</div>");
    }

    private static string RelativePath(string reportPath, string targetPath)
    {
        var reportDirectory = Path.GetDirectoryName(reportPath) ?? AppContext.BaseDirectory;
        return Path.GetRelativePath(reportDirectory, targetPath).Replace('\\', '/');
    }

    private static string Html(string? text)
    {
        return WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(text) ? string.Empty : text);
    }

    private static string HtmlAttr(string text)
    {
        return WebUtility.HtmlEncode(text);
    }

    private static string GetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int? GetInt(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(property.GetString(), out var value)
            ? value
            : null;
    }

    private static string GetRectangle(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var rectangle) || rectangle.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var x = GetInt(rectangle, "X") ?? 0;
        var y = GetInt(rectangle, "Y") ?? 0;
        var width = GetInt(rectangle, "Width") ?? 0;
        var height = GetInt(rectangle, "Height") ?? 0;
        return $"{x},{y},{width},{height}";
    }

    private sealed record CountCropScanRoot(string Directory, string Kind);

    private sealed record CountCropMetadataView(
        string ProfileName,
        int? SlotIndex,
        int? GuessedCount,
        int? CorrectedCount,
        string CropBounds,
        string SourceImagePath,
        DateTimeOffset? TimestampUtc,
        string ScanId,
        string CountMethod);

    private sealed record CountCropReviewItem(
        string Kind,
        string ProfileName,
        string ScanId,
        int? SlotIndex,
        int? GuessedCount,
        int? CorrectedCount,
        string CropBounds,
        string SourcePath,
        DateTimeOffset? TimestampUtc,
        string CountMethod,
        string? RawPath,
        string? CleanedPath,
        string? MetadataPath,
        IReadOnlyList<string> WarningFlags,
        IReadOnlyList<string> InfoFlags);

    private sealed record CountCropQualityFlag(string Text, bool IsInfo);

    private sealed record CountCropForegroundStats(
        int ForegroundPixels,
        double ForegroundRatio,
        int LeftEdge,
        int RightEdge,
        int TopEdge,
        int BottomEdge);
}

internal sealed record CountCropReviewReportResult(
    string ReportPath,
    IReadOnlyList<string> ScannedFolders,
    int CropCount,
    int SuspectCount);
