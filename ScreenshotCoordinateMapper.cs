namespace Poe2PriceChecker;

internal sealed record ScreenshotResolutionProfile(
    string Key,
    string Label,
    Size ScreenshotSize,
    double ScaleX,
    double ScaleY)
{
    public static readonly Size BaseScreenshotSize = new(3840, 2160);

    public static readonly ScreenshotResolutionProfile Base2160p = new(
        "3840x2160",
        "3840x2160",
        BaseScreenshotSize,
        1.0,
        1.0);

    public static readonly ScreenshotResolutionProfile Qhd1440p = new(
        "2560x1440",
        "2560x1440",
        new Size(2560, 1440),
        2.0 / 3.0,
        2.0 / 3.0);

    public static readonly ScreenshotResolutionProfile FullHd1080p = new(
        "1920x1080",
        "1920x1080",
        new Size(1920, 1080),
        0.5,
        0.5);

    public static IReadOnlyList<ScreenshotResolutionProfile> Supported { get; } =
    [
        Base2160p,
        Qhd1440p,
        FullHd1080p
    ];

    public static ScreenshotResolutionProfile Detect(Size screenshotSize)
    {
        if (TryDetect(screenshotSize, out var profile))
        {
            return profile;
        }

        throw new NotSupportedException(
            $"Unsupported screenshot resolution detected: {screenshotSize.Width}x{screenshotSize.Height}. " +
            $"Supported full screenshot sizes are {string.Join(", ", Supported.Select(profile => profile.Label))}. " +
            "Use a full 16:9 uncropped static stash screenshot.");
    }

    public static bool TryDetect(Size screenshotSize, out ScreenshotResolutionProfile profile)
    {
        foreach (var supported in Supported)
        {
            if (supported.ScreenshotSize == screenshotSize)
            {
                profile = supported;
                return true;
            }
        }

        profile = Base2160p;
        return false;
    }

    public static double DetectScaleFromStashCropSize(Size cropSize)
    {
        if (Math.Abs(cropSize.Width - 638) <= 1)
        {
            return FullHd1080p.ScaleX;
        }

        if (Math.Abs(cropSize.Width - 850) <= 1)
        {
            return Qhd1440p.ScaleX;
        }

        return Base2160p.ScaleX;
    }

    public static double DetectScaleOrDefault(Size imageSize)
    {
        return TryDetect(imageSize, out var profile)
            ? profile.ScaleX
            : DetectScaleFromStashCropSize(imageSize);
    }
}

internal sealed class StashCoordinateMapper
{
    public StashCoordinateMapper(ScreenshotResolutionProfile profile)
    {
        Profile = profile;
    }

    public ScreenshotResolutionProfile Profile { get; }

    public static StashCoordinateMapper FromScreenshotSize(Size screenshotSize)
    {
        return new StashCoordinateMapper(ScreenshotResolutionProfile.Detect(screenshotSize));
    }

    public static StashCoordinateMapper Base { get; } = new(ScreenshotResolutionProfile.Base2160p);

    public Rectangle ScaleRectFromBase(Rectangle rectangle)
    {
        return ScaleRectangle(rectangle, Profile.ScaleX, Profile.ScaleY);
    }

    public Rectangle UnscaleRectToBase(Rectangle rectangle)
    {
        return ScaleRectangle(rectangle, 1.0 / Profile.ScaleX, 1.0 / Profile.ScaleY);
    }

    public Point ScalePointFromBase(Point point)
    {
        return new Point(
            (int)Math.Round(point.X * Profile.ScaleX, MidpointRounding.AwayFromZero),
            (int)Math.Round(point.Y * Profile.ScaleY, MidpointRounding.AwayFromZero));
    }

    public int ScaleLengthFromBase(int value)
    {
        return Math.Max(1, (int)Math.Round(value * Profile.ScaleX, MidpointRounding.AwayFromZero));
    }

    public StashLayoutProfile ScaleLayoutFromBase(StashLayoutProfile layout)
    {
        return new StashLayoutProfile(
            ScaleRectFromBase(layout.DisplayCropRegion),
            ScalePointFromBase(layout.SlotOffset));
    }

    public Rectangle OffsetAndScaleRectFromBase(Rectangle rectangle, Point baseOffset)
    {
        return ScaleRectFromBase(new Rectangle(
            rectangle.X + baseOffset.X,
            rectangle.Y + baseOffset.Y,
            rectangle.Width,
            rectangle.Height));
    }

    public Rectangle ToCropBounds(Rectangle actualScreenBounds, StashLayoutProfile actualLayout)
    {
        return new Rectangle(
            actualScreenBounds.X - actualLayout.DisplayCropRegion.X,
            actualScreenBounds.Y - actualLayout.DisplayCropRegion.Y,
            actualScreenBounds.Width,
            actualScreenBounds.Height);
    }

    public string[] BuildDebugLines(
        Size screenshotSize,
        Rectangle sourceBounds,
        StashLayoutProfile requestedBaseLayout,
        StashLayoutProfile actualLayout)
    {
        return
        [
            "Resolution mapper:",
            $"  Screenshot: {screenshotSize.Width}x{screenshotSize.Height}",
            $"  Canonical/base: {ScreenshotResolutionProfile.BaseScreenshotSize.Width}x{ScreenshotResolutionProfile.BaseScreenshotSize.Height}",
            $"  Profile: {Profile.Label} ({Profile.Key})",
            $"  Scale: X={Profile.ScaleX:0.####}, Y={Profile.ScaleY:0.####}",
            $"  Source bounds: {FormatRectangle(sourceBounds)}",
            $"  Requested base crop: {FormatRectangle(requestedBaseLayout.DisplayCropRegion)}",
            $"  Actual scaled crop: {FormatRectangle(actualLayout.DisplayCropRegion)}",
            string.Empty
        ];
    }

    private static Rectangle ScaleRectangle(Rectangle rectangle, double scaleX, double scaleY)
    {
        var left = (int)Math.Round(rectangle.Left * scaleX, MidpointRounding.AwayFromZero);
        var top = (int)Math.Round(rectangle.Top * scaleY, MidpointRounding.AwayFromZero);
        var right = (int)Math.Round(rectangle.Right * scaleX, MidpointRounding.AwayFromZero);
        var bottom = (int)Math.Round(rectangle.Bottom * scaleY, MidpointRounding.AwayFromZero);
        return Rectangle.FromLTRB(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
    }

    private static string FormatRectangle(Rectangle rectangle)
    {
        return $"{rectangle.X},{rectangle.Y},{rectangle.Width},{rectangle.Height}";
    }
}
