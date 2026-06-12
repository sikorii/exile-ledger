namespace Poe2PriceChecker;

internal static class StashSlotVisuals
{
    public static bool HasVisibleQuantityMarker(Bitmap screenshot, Rectangle slotBounds)
    {
        var region = ClampRectangle(
            new Rectangle(slotBounds.X + 4, slotBounds.Y + 4, Math.Min(42, slotBounds.Width - 8), Math.Min(38, slotBounds.Height - 8)),
            screenshot.Size);
        if (region.Width <= 0 || region.Height <= 0)
        {
            return false;
        }

        using var crop = screenshot.Clone(region, screenshot.PixelFormat);
        var brightPixels = 0;
        var sampled = 0;

        for (var y = 0; y < crop.Height; y += 2)
        {
            for (var x = 0; x < crop.Width; x += 2)
            {
                var c = crop.GetPixel(x, y);
                var max = Math.Max(c.R, Math.Max(c.G, c.B));
                var min = Math.Min(c.R, Math.Min(c.G, c.B));
                if (max > 165 && max - min < 85)
                {
                    brightPixels++;
                }

                sampled++;
            }
        }

        return sampled > 0 && brightPixels / (double)sampled > 0.035;
    }

    public static bool HasRuneBodySignal(Bitmap screenshot, Rectangle slotBounds)
    {
        if (CurrencyScanner.IsDefinitelyBlank(screenshot, slotBounds))
        {
            return false;
        }

        var region = ClampRectangle(
            new Rectangle(slotBounds.X + 18, slotBounds.Y + 18, Math.Max(1, slotBounds.Width - 36), Math.Max(1, slotBounds.Height - 36)),
            screenshot.Size);
        if (region.Width <= 0 || region.Height <= 0)
        {
            return false;
        }

        using var crop = screenshot.Clone(region, screenshot.PixelFormat);
        var colorfulPixels = 0;
        var brightPixels = 0;
        var sampled = 0;
        double luminanceTotal = 0;
        double luminanceSquaredTotal = 0;

        for (var y = 0; y < crop.Height; y += 3)
        {
            for (var x = 0; x < crop.Width; x += 3)
            {
                var c = crop.GetPixel(x, y);
                var max = Math.Max(c.R, Math.Max(c.G, c.B));
                var min = Math.Min(c.R, Math.Min(c.G, c.B));
                var luminance = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

                if (max > 44 && max - min > 18)
                {
                    colorfulPixels++;
                }

                if (max > 85)
                {
                    brightPixels++;
                }

                luminanceTotal += luminance;
                luminanceSquaredTotal += luminance * luminance;
                sampled++;
            }
        }

        if (sampled == 0)
        {
            return false;
        }

        var colorfulRatio = colorfulPixels / (double)sampled;
        var brightRatio = brightPixels / (double)sampled;
        var average = luminanceTotal / sampled;
        var variance = Math.Max(0, luminanceSquaredTotal / sampled - average * average);
        var standardDeviation = Math.Sqrt(variance);

        return colorfulRatio > 0.22 ||
            brightRatio > 0.12 ||
            (colorfulRatio > 0.14 && average > 26 && standardDeviation > 18);
    }

    public static bool HasGenericItemSignal(Bitmap screenshot, Rectangle slotBounds)
    {
        if (CurrencyScanner.IsDefinitelyBlank(screenshot, slotBounds))
        {
            return false;
        }

        var insetX = Math.Max(8, slotBounds.Width / 8);
        var insetY = Math.Max(8, slotBounds.Height / 8);
        var region = ClampRectangle(
            new Rectangle(slotBounds.X + insetX, slotBounds.Y + insetY, Math.Max(1, slotBounds.Width - insetX * 2), Math.Max(1, slotBounds.Height - insetY * 2)),
            screenshot.Size);
        if (region.Width <= 0 || region.Height <= 0)
        {
            return false;
        }

        using var crop = screenshot.Clone(region, screenshot.PixelFormat);
        var colorfulPixels = 0;
        var brightPixels = 0;
        var sampled = 0;
        double luminanceTotal = 0;
        double luminanceSquaredTotal = 0;

        for (var y = 0; y < crop.Height; y += 4)
        {
            for (var x = 0; x < crop.Width; x += 4)
            {
                var c = crop.GetPixel(x, y);
                var max = Math.Max(c.R, Math.Max(c.G, c.B));
                var min = Math.Min(c.R, Math.Min(c.G, c.B));
                var luminance = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

                if (max > 48 && max - min > 14)
                {
                    colorfulPixels++;
                }

                if (max > 75)
                {
                    brightPixels++;
                }

                luminanceTotal += luminance;
                luminanceSquaredTotal += luminance * luminance;
                sampled++;
            }
        }

        if (sampled == 0)
        {
            return false;
        }

        var colorfulRatio = colorfulPixels / (double)sampled;
        var brightRatio = brightPixels / (double)sampled;
        var average = luminanceTotal / sampled;
        var variance = Math.Max(0, luminanceSquaredTotal / sampled - average * average);
        var standardDeviation = Math.Sqrt(variance);

        return colorfulRatio > 0.18 ||
            brightRatio > 0.10 ||
            (average > 28 && standardDeviation > 16);
    }

    private static Rectangle ClampRectangle(Rectangle rectangle, Size size)
    {
        var left = Math.Clamp(rectangle.Left, 0, size.Width);
        var top = Math.Clamp(rectangle.Top, 0, size.Height);
        var right = Math.Clamp(rectangle.Right, left, size.Width);
        var bottom = Math.Clamp(rectangle.Bottom, top, size.Height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
}
