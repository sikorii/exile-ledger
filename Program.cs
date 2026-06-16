namespace Poe2PriceChecker;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        AppPaths.EnsureInitialized();

        if (args.Length > 0 && args[0].Equals("--self-test", StringComparison.OrdinalIgnoreCase))
        {
            RunSelfTest(args.Length > 1 ? args[1] : null);
            return;
        }

        if (args.Length > 0 && args[0].Equals("--runeshaping-parser-test", StringComparison.OrdinalIgnoreCase))
        {
            RunRuneshapingParserTest();
            return;
        }

        if (args.Length > 0 && args[0].Equals("--currency-test", StringComparison.OrdinalIgnoreCase))
        {
            RunCurrencyTest(args.Length > 1 ? args[1] : null);
            return;
        }

        if (args.Length > 0 && args[0].Equals("--runes-test", StringComparison.OrdinalIgnoreCase))
        {
            RunRunesTest(args.Length > 1 ? args[1] : null);
            return;
        }

        if (args.Length > 0 && args[0].Equals("--kalguuran-runes-test", StringComparison.OrdinalIgnoreCase))
        {
            RunKalguuranRunesTest(args.Length > 1 ? args[1] : null);
            return;
        }

        if (args.Length > 0 && args[0].Equals("--fixed-stash-test", StringComparison.OrdinalIgnoreCase))
        {
            RunFixedStashTest(
                args.Length > 1 ? args[1] : null,
                args.Length > 2 ? args[2] : null,
                args.Length > 3 ? args[3] : null);
            return;
        }

        if (args.Length > 0 && args[0].Equals("--slot-layout-debug", StringComparison.OrdinalIgnoreCase))
        {
            RunSlotLayoutDebug(
                args.Length > 1 ? args[1] : null,
                args.Length > 2 ? args[2] : null,
                args.Length > 3 ? args[3] : null);
            return;
        }

        if (args.Length > 0 && args[0].Equals("--essence-profile-report", StringComparison.OrdinalIgnoreCase))
        {
            RunEssenceProfileReport();
            return;
        }

        if (args.Length > 0 &&
            (args[0].Equals("--stash-profile-report", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("--layout-profile-report", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("--overlay-profile-report", StringComparison.OrdinalIgnoreCase)))
        {
            RunOverlayProfileReport(args.Length > 1 ? args[1] : "all");
            return;
        }

        if (args.Length > 0 && args[0].Equals("--icon-cache", StringComparison.OrdinalIgnoreCase))
        {
            RunIconCache(args.Any(arg => arg.Equals("--force", StringComparison.OrdinalIgnoreCase)));
            return;
        }

        if (args.Length > 0 && args[0].Equals("--icon-match", StringComparison.OrdinalIgnoreCase))
        {
            RunIconMatch(
                args.Length > 1 ? args[1] : null,
                args.Length > 2 ? args[2] : null,
                args.Length > 3 ? args[3] : null);
            return;
        }

        ApplicationConfiguration.Initialize();
        var mainForm = new MainForm();
        mainForm.Shown += (_, _) => UpdateChecker.CheckForUpdatesAsync(mainForm);
        Application.Run(mainForm);
    }

    private static void RunSelfTest(string? screenshotPath)
    {
        screenshotPath ??= @"C:\Users\maran\OneDrive\Desktop\runeshaping\Screenshot 2026-06-09 092011.png";
        var debugDirectory = AppPaths.DebugDirectory;
        Directory.CreateDirectory(debugDirectory);

        var scanner = new RuneshapingScanner(debugDirectory);
        var result = scanner.ScanFileAsync(screenshotPath, CancellationToken.None).GetAwaiter().GetResult();
        var lines = result.Choices.Select(choice => $"{choice.Color,-6} {choice.DisplayText}")
            .Concat(result.UnpricedRewards.Select(reward => $"N/A    {reward}"))
            .DefaultIfEmpty("No rewards parsed.")
            .ToArray();

        File.WriteAllLines(Path.Combine(debugDirectory, "self-test.txt"), lines);
    }

    private static void RunRuneshapingParserTest()
    {
        var debugDirectory = AppPaths.DebugDirectory;
        Directory.CreateDirectory(debugDirectory);
        var lines = RuneshapingScanner.RunParserSelfTest();
        File.WriteAllLines(Path.Combine(debugDirectory, "runeshaping-parser-test.txt"), lines);
    }

    private static void RunCurrencyTest(string? screenshotPath)
    {
        screenshotPath ??= ProjectPath("Screenshots", "recovered-currency", "currency-01.png");
        var debugDirectory = AppPaths.DebugDirectory;
        Directory.CreateDirectory(debugDirectory);

        var mappings = new CurrencyMappingStore(AppPaths.ConfigFile("currency-mappings.json"));
        var scanner = new CurrencyScanner(debugDirectory, mappings);
        var result = scanner.ScanFileAsync(screenshotPath, CancellationToken.None).GetAwaiter().GetResult();
        var lines = new[]
            {
                $"Total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div",
                $"Known occupied: {result.KnownOccupiedSlots}",
                $"Unknown occupied: {result.UnknownOccupiedSlots}",
                string.Empty
            }
            .Concat(result.TopStacks.Select(stack => stack.DisplayText))
            .DefaultIfEmpty("No currency stacks parsed.")
            .ToArray();

        File.WriteAllLines(Path.Combine(debugDirectory, "currency-test.txt"), lines);
    }

    private static void RunRunesTest(string? screenshotPath)
    {
        screenshotPath ??= ProjectPath("publish", "debug", "stash-tab-captures", "latest-stash-tab-fullscreen.png");
        var debugDirectory = AppPaths.DebugDirectory;
        Directory.CreateDirectory(debugDirectory);

        var mappings = new CurrencyMappingStore(
            AppPaths.ConfigFile("rune-mappings.json"),
            AppPaths.ConfigFile("rune-count-overrides.json"));
        var scanner = new AugmentRuneScanner(debugDirectory, mappings);
        var result = scanner.ScanFileAsync(screenshotPath, CancellationToken.None).GetAwaiter().GetResult();
        var lines = new[]
            {
                $"Total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div",
                $"Known occupied: {result.KnownOccupiedSlots}",
                $"Unknown occupied: {result.UnknownOccupiedSlots}",
                string.Empty,
                "Upgrade Suggestions:"
            }
            .Concat(result.UpgradeSuggestions.Select(suggestion =>
                $"{(suggestion.IsProfitable ? "UPGRADE" : "SKIP"),-7} {suggestion.UpgradeCount}x {suggestion.FromItemName} -> {suggestion.ToItemName}: {suggestion.ProfitExalts:+0.##;-0.##;0} ex"))
            .Concat([string.Empty, "Top Stacks:"])
            .Concat(result.TopStacks.Select(stack => stack.DisplayText))
            .DefaultIfEmpty("No rune stacks parsed.")
            .ToArray();

        File.WriteAllLines(Path.Combine(debugDirectory, "runes-test.txt"), lines);
    }

    private static void RunKalguuranRunesTest(string? screenshotPath)
    {
        screenshotPath ??= ProjectPath("publish", "debug", "stash-tab-captures", "latest-stash-tab-fullscreen.png");
        var debugDirectory = AppPaths.DebugDirectory;
        Directory.CreateDirectory(debugDirectory);

        var mappings = new CurrencyMappingStore(
            AppPaths.ConfigFile("kalguuran-rune-mappings.json"),
            AppPaths.ConfigFile("kalguuran-rune-count-overrides.json"));
        var scanner = new KalguuranRuneScanner(debugDirectory, mappings);
        var result = scanner.ScanFileAsync(screenshotPath, CancellationToken.None, StashLayoutProfile.FolderFull).GetAwaiter().GetResult();
        var lines = new[]
            {
                $"Total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div",
                $"Known occupied: {result.KnownOccupiedSlots}",
                $"Unknown occupied: {result.UnknownOccupiedSlots}",
                string.Empty,
                "Top Stacks:"
            }
            .Concat(result.TopStacks.Select(stack => stack.DisplayText))
            .DefaultIfEmpty("No Kalguuran rune stacks parsed.")
            .ToArray();

        File.WriteAllLines(Path.Combine(debugDirectory, "kalguuran-runes-test.txt"), lines);
    }

    private static void RunFixedStashTest(string? profileKey, string? screenshotPath, string? layoutName)
    {
        profileKey ??= FixedStashScannerProfiles.Abyss.Key;
        screenshotPath ??= ProjectPath("Screenshots", "Stashes", "Abyss.png");
        var profile = FindProfile(profileKey) ?? FixedStashScannerProfiles.Abyss;
        var layout = ParseLayoutProfile(layoutName, StashLayoutProfile.Folder);
        var debugDirectory = AppPaths.DebugDirectory;
        Directory.CreateDirectory(debugDirectory);

        var mappings = new CurrencyMappingStore(
            AppPaths.ConfigFile(profile.MappingFileName),
            AppPaths.ConfigFile(profile.CountOverrideFileName));
        var scanner = new FixedStashScanner(debugDirectory, mappings, profile);
        var result = scanner.ScanFileAsync(screenshotPath, CancellationToken.None, layout).GetAwaiter().GetResult();
        var lines = new[]
            {
                $"Profile: {profile.Label}",
                $"Layout: {LayoutName(layout)}",
                $"Total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div",
                $"Known occupied: {result.KnownOccupiedSlots}",
                $"Unknown occupied: {result.UnknownOccupiedSlots}",
                "Warning: saved scans can contain stale crop/overlay bounds after layout/profile changes. Rescan this tab in the UI before judging current overlays.",
                string.Empty,
                "Top Stacks:"
            }
            .Concat(result.TopStacks.Select(stack => stack.DisplayText))
            .DefaultIfEmpty("No stacks parsed.")
            .ToArray();

        File.WriteAllLines(Path.Combine(debugDirectory, $"fixed-stash-test-{profile.Key}.txt"), lines);
    }

    private static void RunSlotLayoutDebug(string? profileKey, string? screenshotPath, string? layoutName)
    {
        profileKey ??= FixedStashScannerProfiles.Essence.Key;
        screenshotPath ??= ProjectPath("publish", "debug", "essence-fullscreen.png");
        var profile = FindProfile(profileKey) ?? FixedStashScannerProfiles.Essence;
        var layout = ParseLayoutProfile(layoutName, StashLayoutProfile.FolderFull);
        var debugDirectory = AppPaths.DebugDirectory;
        Directory.CreateDirectory(debugDirectory);

        var mappings = new CurrencyMappingStore(
            AppPaths.ConfigFile(profile.MappingFileName),
            AppPaths.ConfigFile(profile.CountOverrideFileName));
        var scanner = new FixedStashScanner(debugDirectory, mappings, profile);
        var result = scanner.ScanFileAsync(screenshotPath, CancellationToken.None, layout).GetAwaiter().GetResult();
        using var screenshot = CurrencyScanner.LoadBitmapWithoutFileLock(screenshotPath);
        var mapper = StashCoordinateMapper.FromScreenshotSize(screenshot.Size);
        var actualLayout = mapper.ScaleLayoutFromBase(layout);
        var outputPath = StashSlotLayoutDebugRenderer.Write(debugDirectory, result, actualLayout, screenshotPath);

        File.WriteAllLines(
            Path.Combine(debugDirectory, $"slot-layout-debug-{profile.Key}.txt"),
            [
                $"Profile: {profile.Label}",
                $"Layout: {LayoutName(layout)}",
                $"Resolution profile: {mapper.Profile.Label}",
                $"Source: {screenshotPath}",
                $"Output: {outputPath}",
                $"Known occupied: {result.KnownOccupiedSlots}",
                $"Unknown occupied: {result.UnknownOccupiedSlots}",
                "Warning: saved scans can contain stale crop/overlay bounds after layout/profile changes. Rescan this tab in the UI before judging current overlays."
            ]);
    }


    private static void RunIconCache(bool forceDownload)
    {
        var cache = PoeNinjaIconCache.CreateDefault();
        var index = cache.BuildAsync(forceDownload, CancellationToken.None).GetAwaiter().GetResult();
        var debugDirectory = AppPaths.DebugDirectory;
        Directory.CreateDirectory(debugDirectory);

        var lines = new[]
            {
                $"Built: {index.BuiltUtc:O}",
                $"League: {index.League}",
                $"Items indexed: {index.ItemCount}",
                $"Downloaded this run: {index.DownloadedCount}",
                $"Failed downloads: {index.FailedDownloadCount}",
                string.Empty,
                "By type:"
            }
            .Concat(index.Items
                .GroupBy(item => item.Type)
                .OrderBy(group => group.Key)
                .Select(group => $"{group.Key}: {group.Count()}"))
            .Concat([
                string.Empty,
                $"Index: {AppPaths.ConfigFile("poe-ninja-icons.json")}",
                $"Icons: {Path.Combine(AppPaths.CacheDirectory, "icons")}"
            ])
            .ToArray();

        File.WriteAllLines(Path.Combine(debugDirectory, "icon-cache.txt"), lines);
    }

    private static void RunEssenceProfileReport()
    {
        var debugDirectory = AppPaths.DebugDirectory;
        Directory.CreateDirectory(debugDirectory);

        var profile = FixedStashScannerProfiles.Essence;
        var mappingStore = new CurrencyMappingStore(
            FixedStashScannerProfiles.ConfigPath(profile.MappingFileName),
            FixedStashScannerProfiles.ConfigPath(profile.CountOverrideFileName));
        var prices = PoeNinjaPrices.FetchAsync(CancellationToken.None).GetAwaiter().GetResult();
        var lines = EssenceStaticIdentity.BuildCompletenessReport(profile, mappingStore, prices);
        File.WriteAllLines(Path.Combine(debugDirectory, "essence-static-profile.txt"), lines);
    }

    private static void RunOverlayProfileReport(string? selector)
    {
        selector = string.IsNullOrWhiteSpace(selector) ? "all" : selector.Trim();
        var debugDirectory = AppPaths.DebugDirectory;
        Directory.CreateDirectory(debugDirectory);

        var latestStore = new LatestStashScanStore(AppPaths.LatestStashScansPath);
        var latestScans = latestStore.Load(FixedStashScannerProfiles.BuiltIn);
        var reportPath = StashOverlayProfileReporter.Write(
            debugDirectory,
            FixedStashScannerProfiles.BuiltIn,
            latestScans,
            selector);

        File.WriteAllLines(
            Path.Combine(debugDirectory, "overlay-profile-report-command.txt"),
            [
                $"Selector: {selector}",
                $"Report: {reportPath}",
                $"Csv: {Path.Combine(debugDirectory, "overlay-profile-report", "overlay-profile-report.csv")}",
                $"Images: {Path.Combine(debugDirectory, "overlay-profile-report")}"
            ]);
    }

    private static void RunIconMatch(string? screenshotPath, string? mode, string? slotText)
    {
        screenshotPath ??= ProjectPath("publish", "debug", "stash-tab-captures", "latest-stash-tab-fullscreen.png");
        mode ??= "currency";

        var debugDirectory = AppPaths.DebugDirectory;
        Directory.CreateDirectory(debugDirectory);

        var cache = PoeNinjaIconCache.CreateDefault();
        var index = cache.LoadOrBuildAsync(CancellationToken.None).GetAwaiter().GetResult();
        var matcher = PoeNinjaIconMatcher.FromIndex(index, LocalIconTemplateStore.CreateDefault());
        using var screenshot = CurrencyScanner.LoadBitmapWithoutFileLock(screenshotPath);
        var mapper = StashCoordinateMapper.FromScreenshotSize(screenshot.Size);
        var allowedTypes = GetIconTypesForMode(mode);

        var profile = FindProfile(mode);
        var slots = profile is not null && profile != FixedStashScannerProfiles.Currency && profile != FixedStashScannerProfiles.AugmentRunes && profile != FixedStashScannerProfiles.KalguuranRunes
            ? profile.Slots.Select((slot, index) => (Index: index, Bounds: mapper.ScaleRectFromBase(slot.Bounds), Section: slot.Section))
            : mode.Equals("runes", StringComparison.OrdinalIgnoreCase)
            ? RuneSlotMap.Slots.Select((slot, index) => (Index: index, Bounds: mapper.ScaleRectFromBase(slot.Bounds), Section: (string?)null))
            : mode.Equals("kalguuran", StringComparison.OrdinalIgnoreCase)
                ? KalguuranRuneSlotMap.Slots.Select((slot, index) => (Index: index, Bounds: mapper.ScaleRectFromBase(slot.Bounds), Section: (string?)null))
                : CurrencySlotMap.Slots.Select((slot, index) => (Index: index, Bounds: mapper.ScaleRectFromBase(slot.Bounds), Section: (string?)null));

        var hasRequestedSlot = int.TryParse(slotText, out var requestedSlot);
        if (hasRequestedSlot)
        {
            slots = slots.Where(slot => slot.Index == requestedSlot);
        }

        var lines = new List<string>
        {
            $"Screenshot: {screenshotPath}",
            $"Resolution profile: {mapper.Profile.Label}",
            $"Mode: {mode}",
            $"Cache items: {index.ItemCount}",
            $"Allowed types: {(allowedTypes is null ? "(all)" : string.Join(", ", allowedTypes))}",
            "Confidence: 0.24 hash + 0.30 histogram + 0.20 edge + 0.26 pixel, plus 0.04 local-template bonus.",
            string.Empty
        };

        foreach (var slot in slots)
        {
            if (!ContainsRectangle(screenshot.Size, slot.Bounds))
            {
                continue;
            }

            var tabKey = profile?.Key ?? mode;
            var context = new IconMatchContext(tabKey, allowedTypes, slot.Section);
            var matches = matcher.MatchSlot(screenshot, slot.Bounds, maxResults: 5, context);
            IconMatchDebugResult? debugResult = null;
            if (hasRequestedSlot)
            {
                debugResult = matcher.WriteDebugForSlot(debugDirectory, mode, slot.Index, screenshot, slot.Bounds, context);
            }

            if (matches.Count == 0)
            {
                lines.Add($"Slot {slot.Index}: no candidates after tab/type filtering.");
                if (debugResult is not null)
                {
                    lines.Add($"  Cleaned crop: {debugResult.CleanedSlotPath}");
                    lines.Add($"  Score report: {debugResult.ReportPath}");
                }

                lines.Add(string.Empty);
                continue;
            }

            lines.Add($"Slot {slot.Index}:");
            if (debugResult is not null)
            {
                lines.Add($"  Cleaned crop: {debugResult.CleanedSlotPath}");
                lines.Add($"  Score report: {debugResult.ReportPath}");
            }

            lines.AddRange(matches.Select(match =>
                $"  {match.Confidence:0.000} gap {match.SecondBestGap:0.000} {match.SourceKind,-14} {match.ItemName} [{match.Type}] hash={match.HashScore:0.000} hist={match.HistogramScore:0.000} edge={match.EdgeScore:0.000} pixel={match.PixelScore:0.000}"));
            lines.Add(string.Empty);
        }

        File.WriteAllLines(Path.Combine(debugDirectory, "icon-match.txt"), lines);
    }

    private static bool ContainsRectangle(Size size, Rectangle rectangle)
    {
        return rectangle.X >= 0 &&
            rectangle.Y >= 0 &&
            rectangle.Right <= size.Width &&
            rectangle.Bottom <= size.Height;
    }

    private static IReadOnlySet<string>? GetIconTypesForMode(string mode)
    {
        var profile = FindProfile(mode);
        if (profile is not null)
        {
            return profile.IconCategories;
        }

        if (mode.Equals("currency", StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<string>(["Currency"], StringComparer.OrdinalIgnoreCase);
        }

        if (mode.Equals("runes", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("kalguuran", StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<string>(["Runes"], StringComparer.OrdinalIgnoreCase);
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static FixedStashScannerProfile? FindProfile(string profileKeyOrLabel)
    {
        return FixedStashScannerProfiles.BuiltIn.FirstOrDefault(profile =>
            profile.Key.Equals(profileKeyOrLabel, StringComparison.OrdinalIgnoreCase) ||
            profile.Label.Equals(profileKeyOrLabel, StringComparison.OrdinalIgnoreCase));
    }

    private static string ProjectPath(params string[] pathParts)
    {
        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var current = baseDirectory; current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "ExileLedger.csproj")))
            {
                return Path.Combine([current.FullName, .. pathParts]);
            }
        }

        return Path.Combine([Directory.GetCurrentDirectory(), .. pathParts]);
    }

    private static StashLayoutProfile ParseLayoutProfile(string? layoutName, StashLayoutProfile fallback)
    {
        if (string.IsNullOrWhiteSpace(layoutName))
        {
            return fallback;
        }

        return layoutName.Trim().ToLowerInvariant() switch
        {
            "normal" => StashLayoutProfile.Normal,
            "folder" => StashLayoutProfile.Folder,
            "folderfull" or "folder-full" => StashLayoutProfile.FolderFull,
            "normalfromfoldermap" or "normal-from-folder-map" => StashLayoutProfile.NormalFromFolderMap,
            "folderfromnormalmap" or "folder-from-normal-map" => StashLayoutProfile.FolderFromNormalMap,
            _ => fallback
        };
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

        return $"{layout.DisplayCropRegion.X},{layout.DisplayCropRegion.Y},{layout.DisplayCropRegion.Width},{layout.DisplayCropRegion.Height}+{layout.SlotOffset.X},{layout.SlotOffset.Y}";
    }
}
