namespace Poe2PriceChecker;

internal static class AppPaths
{
    private const string AppFolderName = "Exile Ledger";
    private const string LegacyAppFolderName = "POE2 Price Checker";
    private const string LatestStashScansFileName = "latest-stash-scans.json";
    private const string SlotLayoutOverridesFileName = "slot-layout-overrides.json";

    private static bool _initialized;
    private static readonly string LocalAppDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string RootDirectory { get; } = Path.Combine(LocalAppDataDirectory, AppFolderName);
    private static string LegacyRootDirectory { get; } = Path.Combine(LocalAppDataDirectory, LegacyAppFolderName);

    public static string ConfigDirectory => Path.Combine(RootDirectory, "config");
    public static string DebugDirectory => Path.Combine(RootDirectory, "debug");
    public static string RuneshapingDebugPath => DebugFile("runeshaping-debug.txt");
    public static string CacheDirectory => Path.Combine(RootDirectory, "cache");
    public static string TrainingDirectory => Path.Combine(RootDirectory, "training");
    public static string LatestStashScansPath => ConfigFile(LatestStashScansFileName);
    public static string SlotLayoutOverridesPath => Path.Combine(RootDirectory, SlotLayoutOverridesFileName);
    public static string? MigrationSourceConfigDirectory { get; private set; }
    public static string? MigrationSourceRootDirectory { get; private set; }

    public static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        var rootDirectoryExists = Directory.Exists(RootDirectory);
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(DebugDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(TrainingDirectory);

        MigrateExistingLocalAppDataIfNeeded(rootDirectoryExists);
        MigrateExistingPublishDataIfNeeded();
        _initialized = true;
    }

    public static string ConfigFile(string fileName)
    {
        return Path.Combine(ConfigDirectory, fileName);
    }

    public static string DebugFile(string fileName)
    {
        return Path.Combine(DebugDirectory, fileName);
    }

    private static void MigrateExistingLocalAppDataIfNeeded(bool rootDirectoryExists)
    {
        if (rootDirectoryExists || !Directory.Exists(LegacyRootDirectory))
        {
            return;
        }

        CopyDirectoryWithoutOverwrite(LegacyRootDirectory, RootDirectory);
        MigrationSourceRootDirectory = LegacyRootDirectory;

        var legacyConfigDirectory = Path.Combine(LegacyRootDirectory, "config");
        if (Directory.Exists(legacyConfigDirectory))
        {
            MigrationSourceConfigDirectory = legacyConfigDirectory;
        }
    }

    private static void MigrateExistingPublishDataIfNeeded()
    {
        if (File.Exists(LatestStashScansPath))
        {
            return;
        }

        var sourceConfigDirectory = FindExistingPublishConfigDirectory();
        if (sourceConfigDirectory is null)
        {
            return;
        }

        var sourceLatest = Path.Combine(sourceConfigDirectory, LatestStashScansFileName);
        if (!File.Exists(sourceLatest))
        {
            return;
        }

        CopyDirectoryWithoutOverwrite(sourceConfigDirectory, ConfigDirectory);
        MigrationSourceConfigDirectory ??= sourceConfigDirectory;

        var sourceRootDirectory = Directory.GetParent(sourceConfigDirectory)?.FullName;
        if (sourceRootDirectory is null)
        {
            return;
        }

        MigrationSourceRootDirectory ??= sourceRootDirectory;
        var sourceLayoutOverrides = Path.Combine(sourceRootDirectory, SlotLayoutOverridesFileName);
        if (File.Exists(sourceLayoutOverrides) && !File.Exists(SlotLayoutOverridesPath))
        {
            File.Copy(sourceLayoutOverrides, SlotLayoutOverridesPath);
        }
    }

    private static string? FindExistingPublishConfigDirectory()
    {
        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var current = baseDirectory; current is not null; current = current.Parent)
        {
            var directConfig = Path.Combine(current.FullName, "config");
            if (current.Name.Equals("publish", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(directConfig, LatestStashScansFileName)))
            {
                return directConfig;
            }

            var publishConfig = Path.Combine(current.FullName, "publish", "config");
            if (File.Exists(Path.Combine(publishConfig, LatestStashScansFileName)))
            {
                return publishConfig;
            }
        }

        return null;
    }

    private static void CopyDirectoryWithoutOverwrite(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            if (File.Exists(destinationPath))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath);
        }
    }
}
