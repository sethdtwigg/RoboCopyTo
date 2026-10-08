using System.Text.Json.Serialization;

namespace RoboCopyTo.Core.Presets;

/// <summary>Stored in %AppData%\RoboCopyTo\settings.json.</summary>
public sealed class AppSettings
{
    public const int MaxRecentDestinations = 10;

    public List<string> RecentDestinations { get; set; } = [];
    public string? DefaultPreset { get; set; }
    public string? LastUsedPreset { get; set; }

    /// <summary>Set when settings.json could not be read.</summary>
    [JsonIgnore]
    public string? LoadWarning { get; private set; }

    public static string DefaultPath => Path.Combine(JsonFiles.AppDataDirectory, "settings.json");

    public static AppSettings Load(string path)
    {
        var s = JsonFiles.Read<AppSettings>(path, out var warning) ?? new AppSettings();
        s.LoadWarning = warning;
        s.RecentDestinations = (s.RecentDestinations ?? []).Where(d => !string.IsNullOrWhiteSpace(d)).Take(MaxRecentDestinations).ToList();
        return s;
    }

    public void Save(string path) => JsonFiles.Write(path, this);

    /// <summary>Moves (or adds) a destination to the top; keeps at most 10.</summary>
    public void AddRecentDestination(string destination)
    {
        var d = destination.Trim();
        if (d.Length == 0)
            return;
        RecentDestinations.RemoveAll(x => string.Equals(x.TrimEnd('\\'), d.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        RecentDestinations.Insert(0, d);
        if (RecentDestinations.Count > MaxRecentDestinations)
            RecentDestinations.RemoveRange(MaxRecentDestinations, RecentDestinations.Count - MaxRecentDestinations);
    }

    /// <summary>The user's default preset, else the last used one, else Quick copy.</summary>
    public string InitialPresetName(PresetStore store)
        => store.Find(DefaultPreset)?.Name ?? store.Find(LastUsedPreset)?.Name ?? Preset.QuickCopy;
}
