namespace RoboCopyTo.Core.Presets;

/// <summary>Built-in presets plus the user's own, stored in %AppData%\RoboCopyTo\presets.json.</summary>
public sealed class PresetStore
{
    private sealed class FileModel
    {
        public List<Preset?>? Presets { get; set; } = [];
    }

    private readonly List<Preset> _custom = [];

    public string FilePath { get; }

    /// <summary>Set when presets.json could not be read; the dialog shows it once.</summary>
    public string? LoadWarning { get; private set; }

    public PresetStore(string filePath)
    {
        FilePath = filePath;
        Reload();
    }

    public static string DefaultPath => Path.Combine(JsonFiles.AppDataDirectory, "presets.json");

    public static PresetStore CreateDefault() => new(DefaultPath);

    /// <summary>Built-ins first, then custom presets in saved order.</summary>
    public IReadOnlyList<Preset> All => [.. Preset.BuiltIns, .. _custom];

    public Preset? Find(string? name)
        => name is null ? null : All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public static bool IsBuiltInName(string name)
        => Preset.BuiltIns.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public void Reload()
    {
        _custom.Clear();
        var model = JsonFiles.Read<FileModel>(FilePath, out var warning);
        LoadWarning = warning;
        foreach (var p in model?.Presets ?? [])
        {
            if (p is null || string.IsNullOrWhiteSpace(p.Name) || p.Options is null || IsBuiltInName(p.Name) || _custom.Any(c => Same(c.Name, p.Name)))
                continue;
            _custom.Add(p with { Name = p.Name.Trim(), Options = p.Options.Sanitized(), IsBuiltIn = false });
        }
    }

    /// <summary>
    /// Adds a new custom preset or replaces the one with the same name. Built-in names are refused.
    /// If the file cannot be written, the in-memory list is left unchanged and the exception propagates.
    /// </summary>
    public Preset Save(Preset preset)
    {
        var name = preset.Name.Trim();
        if (name.Length == 0)
            throw new ArgumentException("A preset needs a name.");
        if (IsBuiltInName(name))
            throw new InvalidOperationException($"\"{name}\" is a built-in preset and cannot be changed. Use Save as to copy it.");
        var saved = preset with { Name = name, Options = preset.Options.Sanitized(), IsBuiltIn = false };
        var next = new List<Preset>(_custom);
        var index = next.FindIndex(c => Same(c.Name, name));
        if (index >= 0)
            next[index] = saved;
        else
            next.Add(saved);
        Commit(next);
        return saved;
    }

    public void Delete(string name)
    {
        if (IsBuiltInName(name))
            throw new InvalidOperationException($"\"{name}\" is a built-in preset and cannot be deleted.");
        var next = _custom.Where(c => !Same(c.Name, name)).ToList();
        if (next.Count != _custom.Count)
            Commit(next);
    }

    /// <summary>Writes first, then updates memory, so the dropdown never shows something the file does not hold.</summary>
    private void Commit(List<Preset> next)
    {
        JsonFiles.Write(FilePath, new FileModel { Presets = [.. next] });
        _custom.Clear();
        _custom.AddRange(next);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
