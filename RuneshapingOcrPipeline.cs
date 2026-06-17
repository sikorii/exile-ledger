using System.Globalization;
using System.Drawing;
using Tesseract;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace Poe2PriceChecker;

internal static class RuneshapingOcrPipeline
{
    public static async Task<RuneshapingOcrResult> RecognizeAsync(
        string processedImagePath,
        string tessDataDirectory,
        CancellationToken cancellationToken)
    {
        var windowsResult = await TryRecognizeWithWindowsOcrAsync(processedImagePath, cancellationToken).ConfigureAwait(false);
        if (windowsResult.Result is not null)
        {
            return windowsResult.Result;
        }

        var tesseractText = RunTesseractOcr(processedImagePath, tessDataDirectory);
        return new RuneshapingOcrResult(
            "tesseract",
            tesseractText,
            [],
            windowsResult.FallbackReason);
    }

    public static IReadOnlyList<string> DescribeWindowsOcrAvailability()
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null)
            {
                return ["Windows OCR available: false", "Reason: OcrEngine.TryCreateFromUserProfileLanguages returned null"];
            }

            return
            [
                "Windows OCR available: true",
                $"Recognizer language: {engine.RecognizerLanguage.DisplayName} ({engine.RecognizerLanguage.LanguageTag})",
                $"Max image dimension: {OcrEngine.MaxImageDimension}"
            ];
        }
        catch (Exception ex)
        {
            return ["Windows OCR available: false", $"Reason: {ex.GetType().Name}: {ex.Message}"];
        }
    }

    private static async Task<(RuneshapingOcrResult? Result, string FallbackReason)> TryRecognizeWithWindowsOcrAsync(
        string processedImagePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null)
            {
                return (null, "Windows OCR unavailable: OcrEngine.TryCreateFromUserProfileLanguages returned null");
            }

            var file = await StorageFile.GetFileFromPathAsync(processedImagePath).AsTask(cancellationToken).ConfigureAwait(false);
            using var stream = await file.OpenReadAsync().AsTask(cancellationToken).ConfigureAwait(false);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
            using var bitmap = await decoder
                .GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (bitmap.PixelWidth > OcrEngine.MaxImageDimension || bitmap.PixelHeight > OcrEngine.MaxImageDimension)
            {
                return (
                    null,
                    $"Windows OCR unavailable: image {bitmap.PixelWidth}x{bitmap.PixelHeight} exceeds max dimension {OcrEngine.MaxImageDimension}");
            }

            var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
            var lines = result.Lines
                .Select((line, index) => ToDebugLine(index + 1, line))
                .ToArray();
            var text = lines.Length == 0
                ? result.Text
                : string.Join(Environment.NewLine, lines.Select(line => line.Text));

            if (string.IsNullOrWhiteSpace(text))
            {
                return (null, "Windows OCR returned no text");
            }

            return (
                new RuneshapingOcrResult(
                    "windows",
                    text,
                    lines,
                    string.Empty),
                string.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, $"Windows OCR failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static RuneshapingOcrLine ToDebugLine(int lineNumber, OcrLine line)
    {
        var words = line.Words
            .Select((word, index) => new RuneshapingOcrWord(
                index + 1,
                word.Text,
                FormatBounds(word.BoundingRect.X, word.BoundingRect.Y, word.BoundingRect.Width, word.BoundingRect.Height)))
            .ToArray();
        var lineBounds = words.Length == 0
            ? (RectangleF?)null
            : GetContainingBounds(line.Words.Select(word => word.BoundingRect));
        var lineBoundsText = lineBounds is null
            ? string.Empty
            : FormatBounds(lineBounds.Value);
        return new RuneshapingOcrLine(lineNumber, line.Text, lineBoundsText, lineBounds, words);
    }

    private static RectangleF GetContainingBounds(IEnumerable<Windows.Foundation.Rect> rectangles)
    {
        var bounds = rectangles.ToArray();
        var left = bounds.Min(rectangle => rectangle.X);
        var top = bounds.Min(rectangle => rectangle.Y);
        var right = bounds.Max(rectangle => rectangle.X + rectangle.Width);
        var bottom = bounds.Max(rectangle => rectangle.Y + rectangle.Height);
        return new RectangleF(
            (float)left,
            (float)top,
            (float)(right - left),
            (float)(bottom - top));
    }

    private static string FormatBounds(RectangleF bounds)
    {
        return FormatBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private static string FormatBounds(double x, double y, double width, double height)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"x={x:0.#},y={y:0.#},w={width:0.#},h={height:0.#}");
    }

    private static string RunTesseractOcr(string imagePath, string tessDataDirectory)
    {
        using var engine = new TesseractEngine(tessDataDirectory, "eng", EngineMode.Default);
        engine.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789' -x");
        engine.DefaultPageSegMode = PageSegMode.SparseText;

        using var pix = Pix.LoadFromFile(imagePath);
        using var page = engine.Process(pix);
        return page.GetText();
    }
}

internal sealed record RuneshapingOcrResult(
    string Backend,
    string Text,
    IReadOnlyList<RuneshapingOcrLine> Lines,
    string FallbackReason);

internal sealed record RuneshapingOcrLine(
    int LineNumber,
    string Text,
    string Bounds,
    RectangleF? BoundsRectangle,
    IReadOnlyList<RuneshapingOcrWord> Words);

internal sealed record RuneshapingOcrWord(
    int WordNumber,
    string Text,
    string Bounds);
