using System.Text.Json;

namespace Poe2PriceChecker;

internal sealed class CurrencyMappingStore
{
    private readonly string _path;
    private readonly string? _defaultMappingPath;
    private readonly string _countOverridePath;
    private readonly Dictionary<int, string> _customNames = new();
    private readonly Dictionary<int, int> _countOverrides = new();

    public CurrencyMappingStore(string path, string? countOverridePath = null, string? defaultMappingPath = null)
    {
        _path = path;
        _defaultMappingPath = defaultMappingPath ?? DefaultMappingPathFor(path);
        _countOverridePath = countOverridePath ??
            Path.Combine(
                Path.GetDirectoryName(path) ?? AppContext.BaseDirectory,
                "currency-count-overrides.json");
        Load();
        LoadCountOverrides();
    }

    public string? GetName(int slotIndex, string? defaultName)
    {
        return _customNames.TryGetValue(slotIndex, out var customName)
            ? customName
            : defaultName;
    }

    public bool IsCustomMapped(int slotIndex)
    {
        return _customNames.ContainsKey(slotIndex);
    }

    public int? GetCountOverride(int slotIndex)
    {
        return _countOverrides.TryGetValue(slotIndex, out var count)
            ? count
            : null;
    }

    public bool IsCountOverridden(int slotIndex)
    {
        return _countOverrides.ContainsKey(slotIndex);
    }

    public void SetName(int slotIndex, string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            _customNames.Remove(slotIndex);
        }
        else
        {
            _customNames[slotIndex] = itemName.Trim();
        }

        Save();
    }

    public void SetCountOverride(int slotIndex, int? quantity)
    {
        if (quantity is null)
        {
            _countOverrides.Remove(slotIndex);
        }
        else
        {
            _countOverrides[slotIndex] = Math.Max(0, quantity.Value);
        }

        SaveCountOverrides();
    }

    private void Load()
    {
        _customNames.Clear();
        LoadNames(_defaultMappingPath);
        LoadNames(_path);
    }

    private void LoadNames(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<Dictionary<int, string>>(json);
            if (data is null)
            {
                return;
            }

            foreach (var (slot, name) in data)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _customNames[slot] = name;
                }
            }
        }
        catch
        {
            // Keep running even if a mapping JSON file is malformed.
        }
    }

    private static string? DefaultMappingPathFor(string path)
    {
        var fileName = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(fileName)
            ? null
            : Path.Combine(AppContext.BaseDirectory, "Data", "default-mappings", fileName);
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(_customNames, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json + Environment.NewLine);
    }

    private void LoadCountOverrides()
    {
        if (!File.Exists(_countOverridePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_countOverridePath);
            var data = JsonSerializer.Deserialize<Dictionary<int, int>>(json);
            if (data is null)
            {
                return;
            }

            _countOverrides.Clear();
            foreach (var (slot, count) in data)
            {
                if (count >= 0)
                {
                    _countOverrides[slot] = count;
                }
            }
        }
        catch
        {
            // Keep running even if the user-edited override JSON is malformed.
        }
    }

    private void SaveCountOverrides()
    {
        var directory = Path.GetDirectoryName(_countOverridePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(_countOverrides, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_countOverridePath, json + Environment.NewLine);
    }
}
