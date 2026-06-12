using System.Text.Json;

namespace Poe2PriceChecker;

internal sealed class StashLayoutSettingsStore
{
    private readonly string _path;
    private readonly Dictionary<string, StashLayoutSetting> _settings = new(StringComparer.OrdinalIgnoreCase);

    public StashLayoutSettingsStore(string path)
    {
        _path = path;
        Load();
    }

    public bool GetInsideFolder(string modeKey)
    {
        return _settings.TryGetValue(modeKey, out var setting) && setting.InsideFolder;
    }

    public bool GetInsideFolder(string modeKey, bool defaultValue)
    {
        return _settings.TryGetValue(modeKey, out var setting)
            ? setting.InsideFolder
            : defaultValue;
    }

    public void SetInsideFolder(string modeKey, bool insideFolder)
    {
        _settings[modeKey] = new StashLayoutSetting(insideFolder);
        Save();
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);
            var data = JsonSerializer.Deserialize<Dictionary<string, StashLayoutSetting>>(json);
            if (data is null)
            {
                return;
            }

            _settings.Clear();
            foreach (var (mode, setting) in data)
            {
                _settings[mode] = setting;
            }
        }
        catch
        {
            // Keep defaults if the user-edited settings JSON is malformed.
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json + Environment.NewLine);
    }
}

internal sealed record StashLayoutSetting(bool InsideFolder);
