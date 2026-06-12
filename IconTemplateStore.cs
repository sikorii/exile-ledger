using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Poe2PriceChecker;

internal sealed record IconMatchContext(
    string TabKey,
    IReadOnlySet<string>? AllowedTypes,
    string? SlotSection = null);

internal sealed record IconMatchScores(
    double HashSimilarity,
    double HistogramSimilarity,
    double EdgeSimilarity,
    double PixelSimilarity)
{
    public double CombinedConfidence =>
        HashSimilarity * 0.24 +
        HistogramSimilarity * 0.30 +
        EdgeSimilarity * 0.20 +
        PixelSimilarity * 0.26;
}

internal sealed record LocalIconTemplateEntry(
    string ItemName,
    string TabKey,
    int SlotIndex,
    string? SlotSection,
    string TemplatePath,
    string? SourceScreenshotPath,
    DateTimeOffset CreatedUtc);

internal sealed class LocalIconTemplateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;
    private readonly string _indexPath;

    public LocalIconTemplateStore(string directory)
    {
        _directory = directory;
        _indexPath = Path.Combine(directory, "templates.json");
    }

    public static LocalIconTemplateStore CreateDefault()
    {
        return new LocalIconTemplateStore(Path.Combine(AppContext.BaseDirectory, "config", "icon-templates"));
    }

    public IReadOnlyList<LocalIconTemplateEntry> Load()
    {
        if (!File.Exists(_indexPath))
        {
            return [];
        }

        try
        {
            var entries = JsonSerializer.Deserialize<List<LocalIconTemplateEntry>>(File.ReadAllText(_indexPath));
            return entries?
                .Where(entry => !string.IsNullOrWhiteSpace(entry.ItemName) && File.Exists(entry.TemplatePath))
                .ToArray() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public string SaveTemplate(
        string stashCropPath,
        Rectangle cropBounds,
        string tabKey,
        int slotIndex,
        string itemName,
        string? slotSection)
    {
        if (string.IsNullOrWhiteSpace(itemName) || !File.Exists(stashCropPath))
        {
            return string.Empty;
        }

        Directory.CreateDirectory(_directory);
        var tabDirectory = Path.Combine(_directory, Slug(tabKey));
        Directory.CreateDirectory(tabDirectory);

        using var stashCrop = CurrencyScanner.LoadBitmapWithoutFileLock(stashCropPath);
        var safeBounds = IconCropPreprocessor.ClampRectangle(cropBounds, stashCrop.Size);
        if (safeBounds.Width < 8 || safeBounds.Height < 8)
        {
            return string.Empty;
        }

        using var template = IconCropPreprocessor.PrepareSlotIcon(stashCrop, safeBounds);
        var fileName = $"{slotIndex:000}-{Slug(itemName)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.png";
        var templatePath = Path.Combine(tabDirectory, fileName);
        CurrencyScanner.SaveBitmap(template, templatePath);

        var entries = Load().ToList();
        entries.RemoveAll(entry =>
            entry.TabKey.Equals(tabKey, StringComparison.OrdinalIgnoreCase) &&
            entry.SlotIndex == slotIndex &&
            entry.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase));
        entries.Add(new LocalIconTemplateEntry(
            itemName.Trim(),
            tabKey,
            slotIndex,
            slotSection,
            templatePath,
            stashCropPath,
            DateTimeOffset.UtcNow));

        File.WriteAllText(_indexPath, JsonSerializer.Serialize(entries, JsonOptions) + Environment.NewLine);
        return templatePath;
    }

    private static string Slug(string value)
    {
        var slug = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "item" : slug;
    }
}

internal static class IconCropPreprocessor
{
    public const int PreparedSize = 96;

    public static Bitmap PrepareSlotIcon(Bitmap source, Rectangle slotBounds)
    {
        var iconBounds = BuildIconOnlyBounds(ClampRectangle(slotBounds, source.Size));
        iconBounds = ClampRectangle(iconBounds, source.Size);
        using var prepared = DrawNormalized(source, iconBounds, cropTransparentBorder: false);

        using var graphics = Graphics.FromImage(prepared);
        using var maskBrush = new SolidBrush(Color.Black);
        graphics.FillRectangle(maskBrush, new Rectangle(0, 0, 39, 31));
        graphics.FillRectangle(maskBrush, new Rectangle(70, 0, 26, 25));

        return NormalizeBrightnessAndContrast(prepared);
    }

    public static Bitmap PrepareRawSlotCrop(Bitmap slotCrop)
    {
        using var prepared = DrawNormalized(slotCrop, new Rectangle(0, 0, slotCrop.Width, slotCrop.Height), cropTransparentBorder: false);
        using var graphics = Graphics.FromImage(prepared);
        using var maskBrush = new SolidBrush(Color.Black);
        graphics.FillRectangle(maskBrush, new Rectangle(0, 0, 39, 31));
        graphics.FillRectangle(maskBrush, new Rectangle(70, 0, 26, 25));
        return NormalizeBrightnessAndContrast(prepared);
    }

    public static Bitmap PrepareReferenceIcon(Bitmap source)
    {
        using var prepared = DrawNormalized(source, new Rectangle(0, 0, source.Width, source.Height), cropTransparentBorder: true);
        return NormalizeBrightnessAndContrast(prepared);
    }

    public static Rectangle BuildIconOnlyBounds(Rectangle slotBounds)
    {
        var leftInset = Math.Max(5, (int)Math.Round(slotBounds.Width * 0.07));
        var topInset = Math.Max(5, (int)Math.Round(slotBounds.Height * 0.07));
        var rightInset = Math.Max(5, (int)Math.Round(slotBounds.Width * 0.06));
        var bottomInset = Math.Max(5, (int)Math.Round(slotBounds.Height * 0.07));

        var inner = Rectangle.FromLTRB(
            slotBounds.Left + leftInset,
            slotBounds.Top + topInset,
            slotBounds.Right - rightInset,
            slotBounds.Bottom - bottomInset);

        return inner.Width < 8 || inner.Height < 8
            ? slotBounds
            : inner;
    }

    public static Rectangle ClampRectangle(Rectangle rectangle, Size size)
    {
        var left = Math.Clamp(rectangle.Left, 0, size.Width);
        var top = Math.Clamp(rectangle.Top, 0, size.Height);
        var right = Math.Clamp(rectangle.Right, left, size.Width);
        var bottom = Math.Clamp(rectangle.Bottom, top, size.Height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static Bitmap DrawNormalized(Bitmap source, Rectangle sourceRect, bool cropTransparentBorder)
    {
        sourceRect = cropTransparentBorder
            ? FindContentBounds(source)
            : sourceRect;

        var output = new Bitmap(PreparedSize, PreparedSize, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(output);
        graphics.Clear(Color.Black);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;

        var scale = Math.Min(PreparedSize / (double)sourceRect.Width, PreparedSize / (double)sourceRect.Height);
        var width = Math.Max(1, (int)Math.Round(sourceRect.Width * scale));
        var height = Math.Max(1, (int)Math.Round(sourceRect.Height * scale));
        var x = (PreparedSize - width) / 2;
        var y = (PreparedSize - height) / 2;
        graphics.DrawImage(source, new Rectangle(x, y, width, height), sourceRect, GraphicsUnit.Pixel);
        return output;
    }

    private static Bitmap NormalizeBrightnessAndContrast(Bitmap source)
    {
        var luminanceValues = new List<double>(PreparedSize * PreparedSize);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var c = source.GetPixel(x, y);
                var lum = Luminance(c) / 255.0;
                var chroma = (Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B))) / 255.0;
                if (lum > 0.045 || chroma > 0.05)
                {
                    luminanceValues.Add(lum);
                }
            }
        }

        if (luminanceValues.Count < 24)
        {
            return (Bitmap)source.Clone();
        }

        var mean = luminanceValues.Average();
        var variance = luminanceValues.Sum(value => Math.Pow(value - mean, 2)) / luminanceValues.Count;
        var std = Math.Max(0.08, Math.Sqrt(variance));

        var output = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var c = source.GetPixel(x, y);
                var lum = Luminance(c) / 255.0;
                var targetLum = Math.Clamp(0.45 + (lum - mean) / std * 0.22, 0, 1);
                var scale = lum <= 0.001 ? targetLum : targetLum / lum;
                output.SetPixel(
                    x,
                    y,
                    Color.FromArgb(
                        Math.Clamp((int)Math.Round(c.R * scale), 0, 255),
                        Math.Clamp((int)Math.Round(c.G * scale), 0, 255),
                        Math.Clamp((int)Math.Round(c.B * scale), 0, 255)));
            }
        }

        return output;
    }

    private static Rectangle FindContentBounds(Bitmap bitmap)
    {
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = 0;
        var bottom = 0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.A < 16)
                {
                    continue;
                }

                var lum = Luminance(c);
                if (lum < 8 && Math.Abs(c.R - c.G) < 4 && Math.Abs(c.G - c.B) < 4)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        return right <= left || bottom <= top
            ? new Rectangle(0, 0, bitmap.Width, bitmap.Height)
            : Rectangle.FromLTRB(left, top, right, bottom);
    }

    internal static double Luminance(Color c)
    {
        return 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
    }
}

internal sealed class IconMatchSignature
{
    private const int PixelSize = 32;
    private const int EdgeSize = 16;
    private readonly double[] _histogram;
    private readonly double[] _pixels;
    private readonly double[] _edges;
    private readonly ulong _hash;

    private IconMatchSignature(ulong hash, double[] histogram, double[] pixels, double[] edges)
    {
        _hash = hash;
        _histogram = histogram;
        _pixels = pixels;
        _edges = edges;
    }

    public static IconMatchSignature FromPrepared(Bitmap prepared)
    {
        return new IconMatchSignature(
            BuildDHash(prepared),
            BuildHistogram(prepared),
            BuildPixels(prepared),
            BuildEdges(prepared));
    }

    public IconMatchScores Compare(IconMatchSignature other)
    {
        var hashSimilarity = 1.0 - HammingDistance(_hash, other._hash) / 64.0;
        var histogramSimilarity = 1.0 - L1Distance(_histogram, other._histogram) / 2.0;
        var edgeSimilarity = 1.0 - L1Distance(_edges, other._edges) / _edges.Length;
        var pixelSimilarity = 1.0 - L1Distance(_pixels, other._pixels) / _pixels.Length;

        return new IconMatchScores(
            Math.Clamp(hashSimilarity, 0, 1),
            Math.Clamp(histogramSimilarity, 0, 1),
            Math.Clamp(edgeSimilarity, 0, 1),
            Math.Clamp(pixelSimilarity, 0, 1));
    }

    private static ulong BuildDHash(Bitmap bitmap)
    {
        using var small = new Bitmap(9, 8, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(small))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(bitmap, 0, 0, 9, 8);
        }

        ulong hash = 0;
        var bit = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                if (IconCropPreprocessor.Luminance(small.GetPixel(x, y)) > IconCropPreprocessor.Luminance(small.GetPixel(x + 1, y)))
                {
                    hash |= 1UL << bit;
                }

                bit++;
            }
        }

        return hash;
    }

    private static double[] BuildHistogram(Bitmap bitmap)
    {
        var histogram = new double[48];
        var total = 0.0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                var max = Math.Max(c.R, Math.Max(c.G, c.B));
                var min = Math.Min(c.R, Math.Min(c.G, c.B));
                var luminance = IconCropPreprocessor.Luminance(c) / 255.0;
                var chroma = (max - min) / 255.0;
                if (luminance < 0.05 && chroma < 0.05)
                {
                    continue;
                }

                var hueBucket = Math.Clamp((int)Math.Floor(c.GetHue() / 360.0 * 16.0), 0, 15);
                var satBucket = 16 + Math.Clamp((int)Math.Floor(c.GetSaturation() * 16.0), 0, 15);
                var lightBucket = 32 + Math.Clamp((int)Math.Floor(luminance * 16.0), 0, 15);
                var weight = 0.25 + luminance + chroma;
                histogram[hueBucket] += weight;
                histogram[satBucket] += weight * 0.7;
                histogram[lightBucket] += weight * 0.8;
                total += weight * 2.5;
            }
        }

        if (total <= 0)
        {
            return histogram;
        }

        for (var i = 0; i < histogram.Length; i++)
        {
            histogram[i] /= total;
        }

        return histogram;
    }

    private static double[] BuildPixels(Bitmap bitmap)
    {
        using var small = new Bitmap(PixelSize, PixelSize, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(small))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(bitmap, 0, 0, PixelSize, PixelSize);
        }

        var values = new double[PixelSize * PixelSize * 3];
        var i = 0;
        for (var y = 0; y < PixelSize; y++)
        {
            for (var x = 0; x < PixelSize; x++)
            {
                var c = small.GetPixel(x, y);
                values[i++] = c.R / 255.0;
                values[i++] = c.G / 255.0;
                values[i++] = c.B / 255.0;
            }
        }

        return values;
    }

    private static double[] BuildEdges(Bitmap bitmap)
    {
        using var small = new Bitmap(EdgeSize, EdgeSize, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(small))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(bitmap, 0, 0, EdgeSize, EdgeSize);
        }

        var gray = new double[EdgeSize, EdgeSize];
        for (var y = 0; y < EdgeSize; y++)
        {
            for (var x = 0; x < EdgeSize; x++)
            {
                gray[x, y] = IconCropPreprocessor.Luminance(small.GetPixel(x, y)) / 255.0;
            }
        }

        var edges = new double[EdgeSize * EdgeSize];
        for (var y = 1; y < EdgeSize - 1; y++)
        {
            for (var x = 1; x < EdgeSize - 1; x++)
            {
                var gx =
                    -gray[x - 1, y - 1] + gray[x + 1, y - 1] +
                    -2 * gray[x - 1, y] + 2 * gray[x + 1, y] +
                    -gray[x - 1, y + 1] + gray[x + 1, y + 1];
                var gy =
                    -gray[x - 1, y - 1] - 2 * gray[x, y - 1] - gray[x + 1, y - 1] +
                    gray[x - 1, y + 1] + 2 * gray[x, y + 1] + gray[x + 1, y + 1];
                edges[y * EdgeSize + x] = Math.Clamp(Math.Sqrt(gx * gx + gy * gy), 0, 1);
            }
        }

        return edges;
    }

    private static double L1Distance(double[] a, double[] b)
    {
        var difference = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            difference += Math.Abs(a[i] - b[i]);
        }

        return difference;
    }

    private static int HammingDistance(ulong left, ulong right)
    {
        var value = left ^ right;
        var count = 0;
        while (value != 0)
        {
            count++;
            value &= value - 1;
        }

        return count;
    }
}
