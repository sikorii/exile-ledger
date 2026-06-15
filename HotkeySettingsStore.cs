using System.Text.Json;
using System.Text.Json.Serialization;

namespace Poe2PriceChecker;

internal sealed class HotkeySettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public HotkeySettingsStore(string path)
    {
        _path = path;
    }

    public HotkeySettings Load()
    {
        if (!File.Exists(_path))
        {
            return HotkeySettings.Default;
        }

        try
        {
            var json = File.ReadAllText(_path);
            var file = JsonSerializer.Deserialize<HotkeySettingsFile>(json, JsonOptions);
            if (file is null)
            {
                return HotkeySettings.Default;
            }

            var runeshaping = HotkeySettings.Default.Runeshaping;
            var scanCurrentStash = HotkeySettings.Default.ScanCurrentStash;

            if (!string.IsNullOrWhiteSpace(file.Runeshaping) &&
                HotkeyBinding.TryParse(file.Runeshaping, out var parsedRuneshaping, out _))
            {
                runeshaping = parsedRuneshaping;
            }

            if (!string.IsNullOrWhiteSpace(file.ScanCurrentStash) &&
                HotkeyBinding.TryParse(file.ScanCurrentStash, out var parsedScanCurrentStash, out _))
            {
                scanCurrentStash = parsedScanCurrentStash;
            }

            var settings = new HotkeySettings(runeshaping, scanCurrentStash);
            return settings.HasDuplicateHotkeys()
                ? HotkeySettings.Default
                : settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return HotkeySettings.Default;
        }
    }

    public bool TrySave(HotkeySettings settings, out Exception? error)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                error = new InvalidOperationException("Hotkey settings path must include a directory.");
                return false;
            }

            Directory.CreateDirectory(directory);

            var file = new HotkeySettingsFile(
                settings.Runeshaping.ToDisplayString(),
                settings.ScanCurrentStash.ToDisplayString());
            var json = JsonSerializer.Serialize(file, JsonOptions);
            var tempPath = Path.Combine(
                directory,
                $"{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");

            File.WriteAllText(tempPath, json + Environment.NewLine);
            File.Move(tempPath, _path, overwrite: true);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = ex;
            return false;
        }
    }

    private sealed record HotkeySettingsFile(
        [property: JsonPropertyName("runeshaping")]
        string? Runeshaping,
        [property: JsonPropertyName("scanCurrentStash")]
        string? ScanCurrentStash);
}

internal sealed record HotkeySettings(
    HotkeyBinding Runeshaping,
    HotkeyBinding ScanCurrentStash)
{
    public static HotkeySettings Default { get; } = new(
        HotkeyBinding.FromKey(Keys.F8),
        HotkeyBinding.FromKey(Keys.F7));

    public bool HasDuplicateHotkeys()
    {
        return Runeshaping.Equals(ScanCurrentStash);
    }
}

internal sealed record HotkeyBinding(
    bool Control,
    bool Alt,
    bool Shift,
    bool Windows,
    Keys Key)
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    public static HotkeyBinding FromKey(Keys key)
    {
        return new HotkeyBinding(false, false, false, false, key);
    }

    public static bool TryParse(string text, out HotkeyBinding hotkey, out string error)
    {
        hotkey = HotkeySettings.Default.Runeshaping;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Hotkey cannot be empty.";
            return false;
        }

        var control = false;
        var alt = false;
        var shift = false;
        var windows = false;
        var key = Keys.None;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "Hotkey cannot be empty.";
            return false;
        }

        foreach (var part in parts)
        {
            var normalized = part.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
            switch (normalized)
            {
                case "CTRL":
                case "CONTROL":
                    control = true;
                    continue;
                case "ALT":
                    alt = true;
                    continue;
                case "SHIFT":
                    shift = true;
                    continue;
                case "WIN":
                case "WINDOWS":
                case "META":
                    windows = true;
                    continue;
            }

            if (key != Keys.None)
            {
                error = "Use only one non-modifier key.";
                return false;
            }

            if (!TryParseKey(normalized, out key))
            {
                error = $"Unknown key '{part}'.";
                return false;
            }
        }

        hotkey = new HotkeyBinding(control, alt, shift, windows, key);
        if (!hotkey.IsValid(out error))
        {
            return false;
        }

        return true;
    }

    public uint ToWindowsModifiers(uint extraModifiers)
    {
        var modifiers = extraModifiers;
        if (Control)
        {
            modifiers |= ModControl;
        }

        if (Alt)
        {
            modifiers |= ModAlt;
        }

        if (Shift)
        {
            modifiers |= ModShift;
        }

        if (Windows)
        {
            modifiers |= ModWin;
        }

        return modifiers;
    }

    public string ToDisplayString()
    {
        var parts = new List<string>();
        if (Control)
        {
            parts.Add("Ctrl");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        if (Windows)
        {
            parts.Add("Win");
        }

        parts.Add(FormatKey(Key));
        return string.Join("+", parts);
    }

    private bool IsValid(out string error)
    {
        error = string.Empty;
        if (Key == Keys.None || IsModifierKey(Key))
        {
            error = "Choose a non-modifier key.";
            return false;
        }

        if (!Control && !Alt && !Shift && !Windows && !IsFunctionKey(Key))
        {
            error = "Use Ctrl, Alt, Shift, or Win unless the key is F1-F24.";
            return false;
        }

        return true;
    }

    private static bool TryParseKey(string normalized, out Keys key)
    {
        key = Keys.None;

        if (normalized.Length == 1 && normalized[0] is >= 'A' and <= 'Z')
        {
            return Enum.TryParse(normalized, ignoreCase: true, out key);
        }

        if (normalized.Length == 1 && normalized[0] is >= '0' and <= '9')
        {
            key = normalized[0] switch
            {
                '0' => Keys.D0,
                '1' => Keys.D1,
                '2' => Keys.D2,
                '3' => Keys.D3,
                '4' => Keys.D4,
                '5' => Keys.D5,
                '6' => Keys.D6,
                '7' => Keys.D7,
                '8' => Keys.D8,
                '9' => Keys.D9,
                _ => Keys.None
            };
            return true;
        }

        if (normalized.Length is 2 or 3 &&
            normalized[0] == 'F' &&
            int.TryParse(normalized[1..], out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            key = (Keys)((int)Keys.F1 + functionKey - 1);
            return true;
        }

        key = normalized switch
        {
            "ESC" => Keys.Escape,
            "ESCAPE" => Keys.Escape,
            "ENTER" => Keys.Enter,
            "RETURN" => Keys.Enter,
            "SPACE" => Keys.Space,
            "TAB" => Keys.Tab,
            "BACKSPACE" => Keys.Back,
            "BACK" => Keys.Back,
            "DELETE" => Keys.Delete,
            "DEL" => Keys.Delete,
            "INSERT" => Keys.Insert,
            "INS" => Keys.Insert,
            "HOME" => Keys.Home,
            "END" => Keys.End,
            "PAGEUP" => Keys.PageUp,
            "PAGEDOWN" => Keys.PageDown,
            "UP" => Keys.Up,
            "DOWN" => Keys.Down,
            "LEFT" => Keys.Left,
            "RIGHT" => Keys.Right,
            _ => Keys.None
        };

        if (key != Keys.None)
        {
            return true;
        }

        return Enum.TryParse(normalized, ignoreCase: true, out key) && key != Keys.None;
    }

    private static string FormatKey(Keys key)
    {
        return key switch
        {
            Keys.D0 => "0",
            Keys.D1 => "1",
            Keys.D2 => "2",
            Keys.D3 => "3",
            Keys.D4 => "4",
            Keys.D5 => "5",
            Keys.D6 => "6",
            Keys.D7 => "7",
            Keys.D8 => "8",
            Keys.D9 => "9",
            Keys.Escape => "Esc",
            Keys.Enter => "Enter",
            Keys.Back => "Backspace",
            Keys.PageUp => "PageUp",
            Keys.PageDown => "PageDown",
            _ => key.ToString()
        };
    }

    private static bool IsFunctionKey(Keys key)
    {
        return key is >= Keys.F1 and <= Keys.F24;
    }

    private static bool IsModifierKey(Keys key)
    {
        return key is Keys.Control or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.Shift or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.Alt or Keys.Menu or Keys.LMenu or Keys.RMenu
            or Keys.LWin or Keys.RWin;
    }
}

internal sealed record HotkeySettingsSaveResult(
    bool Saved,
    string RuneshapingHotkey,
    string ScanCurrentStashHotkey,
    string Status);

internal sealed record HotkeyRegistrationResult(
    bool Succeeded,
    string Status);
