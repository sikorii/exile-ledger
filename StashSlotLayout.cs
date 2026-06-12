namespace Poe2PriceChecker;

internal sealed record StashSlotRect(
    int SlotIndex,
    Rectangle FullBounds,
    Rectangle OverlayBounds,
    string? ItemName = null,
    string? Section = null,
    FixedStashSlotIdentity? StaticIdentity = null);

internal sealed record StashGridDescriptor(
    string Section,
    int X,
    int Y,
    int Columns,
    int Rows,
    int CellWidth,
    int CellHeight,
    int StepX,
    int StepY,
    IReadOnlySet<Point>? OmittedCells = null)
{
    public IEnumerable<Rectangle> Generate()
    {
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                if (OmittedCells?.Contains(new Point(column, row)) == true)
                {
                    continue;
                }

                yield return new Rectangle(
                    X + column * StepX,
                    Y + row * StepY,
                    CellWidth,
                    CellHeight);
            }
        }
    }
}

internal sealed record FixedTabLayoutDescriptor(
    string Key,
    IReadOnlyList<StashSlotRect> Slots,
    int DefaultOverlayInset = 0)
{
    public static FixedTabLayoutDescriptor FromSlots(
        string key,
        IReadOnlyList<FixedStashSlot> slots,
        int overlayInset = 0)
    {
        return new FixedTabLayoutDescriptor(
            key,
            slots.Select((slot, index) => new StashSlotRect(
                index,
                slot.Bounds,
                slot.GetOverlayBounds(overlayInset),
                slot.ItemName,
                slot.Section,
                slot.StaticIdentity)).ToArray(),
            overlayInset);
    }

    public static FixedTabLayoutDescriptor FromGridSections(
        string key,
        int overlayInset,
        params StashGridDescriptor[] sections)
    {
        var slots = new List<StashSlotRect>();
        foreach (var section in sections)
        {
            foreach (var bounds in section.Generate())
            {
                slots.Add(new StashSlotRect(
                    slots.Count,
                    bounds,
                    FixedStashSlot.Inset(bounds, overlayInset),
                    Section: section.Section));
            }
        }

        return new FixedTabLayoutDescriptor(key, slots, overlayInset);
    }

    public IReadOnlyList<FixedStashSlot> ToFixedSlots()
    {
        return Slots
            .Select(slot => new FixedStashSlot(slot.FullBounds, slot.ItemName, slot.OverlayBounds, slot.Section, slot.StaticIdentity))
            .ToArray();
    }
}

internal static class StashSlotLayoutDebugRenderer
{
    public static string Write(
        string debugDirectory,
        FixedStashScanResult result,
        StashLayoutProfile layout,
        string sourceScreenshotPath)
    {
        var outputDirectory = Path.Combine(debugDirectory, "slot-layout");
        Directory.CreateDirectory(outputDirectory);

        if (!File.Exists(result.StashCropPath))
        {
            throw new FileNotFoundException("The scanner did not write a stash crop for layout debugging.", result.StashCropPath);
        }

        using var image = CurrencyScanner.LoadBitmapWithoutFileLock(result.StashCropPath);
        using var graphics = Graphics.FromImage(image);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var cropPen = new Pen(Color.FromArgb(255, 80, 255, 120), 3);
        using var slotPen = new Pen(Color.FromArgb(210, 60, 170, 255), 2);
        using var occupiedPen = new Pen(Color.FromArgb(235, 255, 205, 60), 3);
        using var finalPen = new Pen(Color.FromArgb(240, 255, 80, 190), 2);
        using var centerPen = new Pen(Color.FromArgb(220, 255, 255, 255), 1);
        using var labelFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var labelBrush = new SolidBrush(Color.White);
        using var labelBack = new SolidBrush(Color.FromArgb(190, 0, 0, 0));

        graphics.DrawRectangle(cropPen, new Rectangle(1, 1, image.Width - 3, image.Height - 3));

        foreach (var slot in result.Slots)
        {
            var full = slot.CropBounds;
            var final = slot.OverlayCropBounds ?? slot.CropBounds;
            var pen = slot.Occupied ? occupiedPen : slotPen;
            graphics.DrawRectangle(pen, full);
            graphics.DrawRectangle(finalPen, final);

            var center = new Point(full.Left + full.Width / 2, full.Top + full.Height / 2);
            graphics.DrawLine(centerPen, center.X - 5, center.Y, center.X + 5, center.Y);
            graphics.DrawLine(centerPen, center.X, center.Y - 5, center.X, center.Y + 5);

            var label = slot.Occupied
                ? $"{slot.SlotIndex}{(slot.ItemName is null ? "?" : "K")}"
                : slot.SlotIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var labelRect = new Rectangle(full.Left, Math.Max(0, full.Top - 18), Math.Max(24, label.Length * 10 + 8), 18);
            graphics.FillRectangle(labelBack, labelRect);
            graphics.DrawString(label, labelFont, labelBrush, labelRect.Left + 2, labelRect.Top);
        }

        var safeKey = result.Profile.Key.Replace(' ', '-');
        var outputPath = Path.Combine(outputDirectory, $"{safeKey}-layout-debug.png");
        CurrencyScanner.SaveBitmap(image, outputPath);

        var notePath = Path.Combine(outputDirectory, $"{safeKey}-layout-debug.txt");
        var noteLines = new List<string>
        {
            $"Profile: {result.Profile.Label}",
            $"Source screenshot: {sourceScreenshotPath}",
            $"Crop: {layout.DisplayCropRegion.X},{layout.DisplayCropRegion.Y},{layout.DisplayCropRegion.Width},{layout.DisplayCropRegion.Height}",
            $"Slot offset: {layout.SlotOffset.X},{layout.SlotOffset.Y}",
            $"Slots: {result.Slots.Count}",
            $"Known occupied: {result.KnownOccupiedSlots}",
            $"Unknown occupied: {result.UnknownOccupiedSlots}",
            "Legend: green crop border, blue canonical full slots, yellow occupied full slots, magenta final inset overlay, white grid centers.",
            "Warning: publish/config/latest-stash-scans.json can contain stale crop/overlay bounds after layout/profile changes. Rescan the tab in the UI before judging current overlays.",
            string.Empty,
            "Slots:",
            "index,state,fullCropBounds,overlayCropBounds,quantity,item"
        };

        noteLines.AddRange(result.Slots.Select(slot =>
        {
            var overlay = slot.OverlayCropBounds ?? slot.CropBounds;
            var item = slot.ItemName ?? string.Empty;
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{slot.SlotIndex},{(slot.Occupied ? "occupied" : "empty")},{FormatRectangle(slot.CropBounds)},{FormatRectangle(overlay)},{slot.Quantity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty},{item}");
        }));

        File.WriteAllLines(notePath, noteLines);

        return outputPath;
    }

    private static string FormatRectangle(Rectangle rectangle)
    {
        return $"{rectangle.X}:{rectangle.Y}:{rectangle.Width}:{rectangle.Height}";
    }
}

internal static class StashOverlayProfileReporter
{
    public static string Write(
        string debugDirectory,
        IReadOnlyList<FixedStashScannerProfile> profiles,
        LatestStashScanSnapshot latestScans,
        string selector)
    {
        var outputDirectory = Path.Combine(debugDirectory, "overlay-profile-report");
        Directory.CreateDirectory(outputDirectory);

        var selectedProfiles = SelectProfiles(profiles, selector).ToArray();
        var summaryLines = new List<string>
        {
            $"Overlay Profile Report",
            $"Generated: {DateTimeOffset.Now:O}",
            $"Selector: {selector}",
            $"Profiles: {selectedProfiles.Length}",
            $"Output directory: {outputDirectory}",
            string.Empty
        };
        var csvLines = new List<string>
        {
            "profileKey,profileLabel,layout,slotIndex,section,state,scanCropBounds,overlayCropBounds,slotCenter,drawnRectangle,quantity,item,warnings"
        };

        foreach (var profile in selectedProfiles)
        {
            var layout = DefaultLayoutFor(profile);
            var layoutName = LayoutName(layout);
            var latest = LatestSlotsFor(profile, latestScans);
            var imagePath = WriteProfileImage(outputDirectory, profile, layout, latest);
            var slotReports = BuildSlotReports(profile, layout, latest).ToArray();
            var missingOverlay = slotReports.Count(slot => slot.MissingOverlay);
            var sameBounds = slotReports.Count(slot => slot.OverlayEqualsScan);
            var shiftedCenters = slotReports.Count(slot => slot.CenterShift != Point.Empty);
            var uniqueSizes = slotReports
                .Select(slot => $"{slot.OverlayCropBounds.Width}x{slot.OverlayCropBounds.Height}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            summaryLines.Add($"Profile: {profile.Label} ({profile.Key})");
            summaryLines.Add($"Layout: {layoutName}");
            summaryLines.Add($"Slots: {profile.Slots.Count}");
            summaryLines.Add($"Latest saved status: {(latest.Count == 0 ? "none" : $"{latest.Count} slots")}");
            summaryLines.Add($"OverlayCropBounds missing: {missingOverlay}");
            summaryLines.Add($"Overlay equals scan bounds: {sameBounds}");
            summaryLines.Add($"Overlay center shifted from scan center: {shiftedCenters}");
            summaryLines.Add($"Overlay sizes: {string.Join(", ", uniqueSizes)}");
            summaryLines.Add($"Debug image: {imagePath}");
            summaryLines.Add(string.Empty);

            foreach (var slot in slotReports)
            {
                csvLines.Add(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{Escape(profile.Key)},{Escape(profile.Label)},{Escape(layoutName)},{slot.SlotIndex},{Escape(slot.Section ?? string.Empty)},{slot.State},{FormatRectangle(slot.ScanCropBounds)},{FormatRectangle(slot.OverlayCropBounds)},{slot.Center.X}:{slot.Center.Y},{FormatRectangle(slot.OverlayCropBounds)},{slot.Quantity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty},{Escape(slot.ItemName ?? string.Empty)},{Escape(string.Join("|", slot.Warnings))}"));
            }
        }

        var summaryPath = Path.Combine(outputDirectory, "overlay-profile-report.txt");
        var csvPath = Path.Combine(outputDirectory, "overlay-profile-report.csv");
        File.WriteAllLines(summaryPath, summaryLines);
        File.WriteAllLines(csvPath, csvLines);
        return summaryPath;
    }

    private static IEnumerable<FixedStashScannerProfile> SelectProfiles(
        IReadOnlyList<FixedStashScannerProfile> profiles,
        string selector)
    {
        if (selector.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return profiles;
        }

        return profiles.Where(profile =>
            profile.Key.Equals(selector, StringComparison.OrdinalIgnoreCase) ||
            profile.Label.Equals(selector, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyDictionary<int, SavedSlotState> LatestSlotsFor(
        FixedStashScannerProfile profile,
        LatestStashScanSnapshot latestScans)
    {
        if (profile == FixedStashScannerProfiles.Currency &&
            latestScans.Currency.TryGetValue(profile.Key, out var currency))
        {
            return currency.Slots.ToDictionary(
                slot => slot.SlotIndex,
                slot => new SavedSlotState(
                    slot.Occupied,
                    slot.ItemName,
                    slot.Quantity,
                    slot.OverlayCropBounds),
                EqualityComparer<int>.Default);
        }

        if ((profile == FixedStashScannerProfiles.AugmentRunes ||
            profile == FixedStashScannerProfiles.KalguuranRunes) &&
            latestScans.Runes.TryGetValue(profile.Key, out var runes))
        {
            return runes.Slots.ToDictionary(
                slot => slot.SlotIndex,
                slot => new SavedSlotState(
                    slot.Occupied,
                    slot.ItemName,
                    slot.Quantity,
                    slot.OverlayCropBounds),
                EqualityComparer<int>.Default);
        }

        if (latestScans.Generic.TryGetValue(profile.Key, out var generic))
        {
            return generic.Slots.ToDictionary(
                slot => slot.SlotIndex,
                slot => new SavedSlotState(
                    slot.Occupied,
                    slot.ItemName,
                    slot.Quantity,
                    slot.OverlayCropBounds),
                EqualityComparer<int>.Default);
        }

        return new Dictionary<int, SavedSlotState>();
    }

    private static IEnumerable<SlotReport> BuildSlotReports(
        FixedStashScannerProfile profile,
        StashLayoutProfile layout,
        IReadOnlyDictionary<int, SavedSlotState> latest)
    {
        for (var slotIndex = 0; slotIndex < profile.Slots.Count; slotIndex++)
        {
            var slot = profile.Slots[slotIndex];
            var scan = ToCropBounds(slot.Bounds, layout);
            var overlayBounds = slot.GetOverlayBounds(FixedStashScannerProfiles.DefaultStaticOverlayInset);
            var overlay = ToCropBounds(overlayBounds, layout);
            var center = new Point(scan.Left + scan.Width / 2, scan.Top + scan.Height / 2);
            var overlayCenter = new Point(overlay.Left + overlay.Width / 2, overlay.Top + overlay.Height / 2);
            latest.TryGetValue(slotIndex, out var state);
            var warnings = new List<string>();

            if (slot.OverlayBounds is null)
            {
                warnings.Add("missing-overlay");
            }

            if (overlay == scan)
            {
                warnings.Add("overlay-equals-scan");
            }

            var centerShift = new Point(overlayCenter.X - center.X, overlayCenter.Y - center.Y);
            if (centerShift != Point.Empty)
            {
                warnings.Add($"center-shift-{centerShift.X}:{centerShift.Y}");
            }

            if (state is { OverlayCropBounds: null })
            {
                warnings.Add("latest-scan-missing-overlay");
            }
            else if (state?.OverlayCropBounds == scan)
            {
                warnings.Add("latest-overlay-may-equal-scan");
            }

            yield return new SlotReport(
                slotIndex,
                slot.Section,
                state is null
                    ? "not-saved"
                    : state.Occupied
                        ? string.IsNullOrWhiteSpace(state.ItemName) ? "occupied-unknown" : "occupied-known"
                        : string.IsNullOrWhiteSpace(state.ItemName) ? "empty" : "empty-known",
                scan,
                overlay,
                center,
                centerShift,
                state?.ItemName,
                state?.Quantity,
                slot.OverlayBounds is null,
                overlay == scan,
                warnings);
        }
    }

    private static string WriteProfileImage(
        string outputDirectory,
        FixedStashScannerProfile profile,
        StashLayoutProfile layout,
        IReadOnlyDictionary<int, SavedSlotState> latest)
    {
        var sourcePath = LatestCropPathFor(profile);
        using var image = File.Exists(sourcePath)
            ? CurrencyScanner.LoadBitmapWithoutFileLock(sourcePath)
            : new Bitmap(layout.DisplayCropRegion.Width, layout.DisplayCropRegion.Height);

        if (!File.Exists(sourcePath))
        {
            using var background = Graphics.FromImage(image);
            background.Clear(Color.FromArgb(22, 24, 28));
        }

        using var graphics = Graphics.FromImage(image);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var scanPen = new Pen(Color.FromArgb(160, 70, 155, 255), 2);
        using var emptyOverlayPen = new Pen(Color.FromArgb(220, 160, 160, 160), 2);
        using var knownPen = new Pen(Color.FromArgb(235, 80, 210, 130), 3);
        using var unknownPen = new Pen(Color.FromArgb(235, 255, 205, 60), 3);
        using var centerPen = new Pen(Color.FromArgb(200, 255, 255, 255), 1);
        using var labelFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        using var labelBrush = new SolidBrush(Color.White);
        using var labelBack = new SolidBrush(Color.FromArgb(185, 0, 0, 0));

        for (var slotIndex = 0; slotIndex < profile.Slots.Count; slotIndex++)
        {
            var slot = profile.Slots[slotIndex];
            var scan = ToCropBounds(slot.Bounds, layout);
            var overlay = ToCropBounds(slot.GetOverlayBounds(FixedStashScannerProfiles.DefaultStaticOverlayInset), layout);
            latest.TryGetValue(slotIndex, out var state);
            var overlayPen = state is { Occupied: true }
                ? string.IsNullOrWhiteSpace(state.ItemName) ? unknownPen : knownPen
                : emptyOverlayPen;

            graphics.DrawRectangle(scanPen, scan);
            graphics.DrawRectangle(overlayPen, overlay);

            var center = new Point(scan.Left + scan.Width / 2, scan.Top + scan.Height / 2);
            graphics.DrawLine(centerPen, center.X - 5, center.Y, center.X + 5, center.Y);
            graphics.DrawLine(centerPen, center.X, center.Y - 5, center.X, center.Y + 5);

            var label = state is { Occupied: true }
                ? $"{slotIndex}{(string.IsNullOrWhiteSpace(state.ItemName) ? "?" : "K")}"
                : slotIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var labelRect = new Rectangle(scan.Left, Math.Max(0, scan.Top - 18), Math.Max(24, label.Length * 10 + 8), 18);
            graphics.FillRectangle(labelBack, labelRect);
            graphics.DrawString(label, labelFont, labelBrush, labelRect.Left + 2, labelRect.Top);
        }

        var imagePath = Path.Combine(outputDirectory, $"{SafeFileName(profile.Key)}-overlay-profile.png");
        CurrencyScanner.SaveBitmap(image, imagePath);
        return imagePath;

        string LatestCropPathFor(FixedStashScannerProfile current)
        {
            if (current == FixedStashScannerProfiles.Currency)
            {
                return Path.Combine(AppContext.BaseDirectory, "debug", "currency-stash-crop.png");
            }

            if (current == FixedStashScannerProfiles.AugmentRunes)
            {
                return Path.Combine(AppContext.BaseDirectory, "debug", "runes-stash-crop.png");
            }

            if (current == FixedStashScannerProfiles.KalguuranRunes)
            {
                return Path.Combine(AppContext.BaseDirectory, "debug", "kalguuran-runes-stash-crop.png");
            }

            return Path.Combine(AppContext.BaseDirectory, "debug", $"{current.CountMode}-stash-crop.png");
        }
    }

    private static Rectangle ToCropBounds(Rectangle bounds, StashLayoutProfile layout)
    {
        return new Rectangle(
            bounds.X - layout.DisplayCropRegion.X,
            bounds.Y - layout.DisplayCropRegion.Y,
            bounds.Width,
            bounds.Height);
    }

    private static StashLayoutProfile DefaultLayoutFor(FixedStashScannerProfile profile)
    {
        if (profile == FixedStashScannerProfiles.Currency)
        {
            return StashLayoutProfile.Normal;
        }

        if (profile == FixedStashScannerProfiles.AugmentRunes)
        {
            return StashLayoutProfile.Folder;
        }

        if (profile == FixedStashScannerProfiles.KalguuranRunes)
        {
            return StashLayoutProfile.FolderFull;
        }

        return profile.DefaultInsideFolder
            ? StashLayoutProfile.FolderFull
            : StashLayoutProfile.Normal;
    }

    private static string LayoutName(StashLayoutProfile layout)
    {
        if (layout == StashLayoutProfile.Normal)
        {
            return nameof(StashLayoutProfile.Normal);
        }

        if (layout == StashLayoutProfile.Folder)
        {
            return nameof(StashLayoutProfile.Folder);
        }

        if (layout == StashLayoutProfile.FolderFull)
        {
            return nameof(StashLayoutProfile.FolderFull);
        }

        if (layout == StashLayoutProfile.NormalFromFolderMap)
        {
            return nameof(StashLayoutProfile.NormalFromFolderMap);
        }

        if (layout == StashLayoutProfile.FolderFromNormalMap)
        {
            return nameof(StashLayoutProfile.FolderFromNormalMap);
        }

        return $"{layout.DisplayCropRegion.X}:{layout.DisplayCropRegion.Y}:{layout.DisplayCropRegion.Width}:{layout.DisplayCropRegion.Height}+{layout.SlotOffset.X}:{layout.SlotOffset.Y}";
    }

    private static string FormatRectangle(Rectangle rectangle)
    {
        return $"{rectangle.X}:{rectangle.Y}:{rectangle.Width}:{rectangle.Height}";
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string SafeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '-');
        }

        return value.Replace(' ', '-');
    }

    private sealed record SavedSlotState(
        bool Occupied,
        string? ItemName,
        int? Quantity,
        Rectangle? OverlayCropBounds);

    private sealed record SlotReport(
        int SlotIndex,
        string? Section,
        string State,
        Rectangle ScanCropBounds,
        Rectangle OverlayCropBounds,
        Point Center,
        Point CenterShift,
        string? ItemName,
        int? Quantity,
        bool MissingOverlay,
        bool OverlayEqualsScan,
        IReadOnlyList<string> Warnings);
}
