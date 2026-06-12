using System.Text.Json;
using System.Text.Json.Serialization;

namespace Poe2PriceChecker;

internal sealed class SlotLayoutOverrideStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;

    public SlotLayoutOverrideStore(string path)
    {
        _path = path;
    }

    public SlotLayoutOverrides Load()
    {
        if (!File.Exists(_path))
        {
            return new SlotLayoutOverrides();
        }

        try
        {
            using var stream = File.OpenRead(_path);
            var persisted = JsonSerializer.Deserialize<PersistedSlotLayoutOverrides>(stream, JsonOptions);
            if (persisted is null)
            {
                return new SlotLayoutOverrides();
            }

            var overrides = new SlotLayoutOverrides();
            foreach (var profile in persisted.Profiles)
            {
                foreach (var slot in profile.Value)
                {
                    overrides.Set(profile.Key, slot.Key, slot.Value.ToRectangle());
                }
            }

            return overrides;
        }
        catch
        {
            return new SlotLayoutOverrides();
        }
    }

    public void Save(SlotLayoutOverrides overrides)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var persisted = new PersistedSlotLayoutOverrides(
            overrides.ProfileKeys.ToDictionary(
                profileKey => profileKey,
                profileKey => overrides.GetProfile(profileKey)
                    .ToDictionary(pair => pair.Key, pair => Rect.From(pair.Value)),
                StringComparer.OrdinalIgnoreCase));

        var tempPath = Path.Combine(
            directory ?? AppContext.BaseDirectory,
            $"{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");

        File.WriteAllText(tempPath, JsonSerializer.Serialize(persisted, JsonOptions));
        File.Move(tempPath, _path, overwrite: true);
    }

    private sealed record PersistedSlotLayoutOverrides(Dictionary<string, Dictionary<int, Rect>> Profiles);

    private sealed record Rect(int X, int Y, int Width, int Height)
    {
        public static Rect From(Rectangle rectangle)
        {
            return new Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
        }

        public Rectangle ToRectangle()
        {
            return new Rectangle(X, Y, Math.Max(1, Width), Math.Max(1, Height));
        }
    }
}

internal sealed class SlotLayoutOverrides
{
    private readonly Dictionary<string, Dictionary<int, Rectangle>> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> ProfileKeys => _profiles.Keys;

    public bool TryGet(string profileKey, int slotIndex, out Rectangle bounds)
    {
        if (_profiles.TryGetValue(profileKey, out var slots) &&
            slots.TryGetValue(slotIndex, out bounds))
        {
            return true;
        }

        bounds = Rectangle.Empty;
        return false;
    }

    public void Set(string profileKey, int slotIndex, Rectangle bounds)
    {
        if (!_profiles.TryGetValue(profileKey, out var slots))
        {
            slots = new Dictionary<int, Rectangle>();
            _profiles[profileKey] = slots;
        }

        slots[slotIndex] = new Rectangle(bounds.X, bounds.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
    }

    public bool Remove(string profileKey, int slotIndex)
    {
        if (!_profiles.TryGetValue(profileKey, out var slots))
        {
            return false;
        }

        var removed = slots.Remove(slotIndex);
        if (slots.Count == 0)
        {
            _profiles.Remove(profileKey);
        }

        return removed;
    }

    public bool RemoveProfile(string profileKey)
    {
        return _profiles.Remove(profileKey);
    }

    public IReadOnlyDictionary<int, Rectangle> GetProfile(string profileKey)
    {
        return _profiles.TryGetValue(profileKey, out var slots)
            ? slots
            : new Dictionary<int, Rectangle>();
    }
}
