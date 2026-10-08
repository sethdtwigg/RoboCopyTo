namespace RoboCopyTo.Core.Planning;

public enum ItemKind { File, Folder, DriveRoot }

public sealed record SelectedItem(string Path, ItemKind Kind)
{
    public bool IsLooseFile => Kind == ItemKind.File;
}

public sealed record SelectionAnalysis(IReadOnlyList<SelectedItem> Items)
{
    public bool HasLooseFiles => Items.Any(i => i.Kind == ItemKind.File);
    public bool HasDriveRoots => Items.Any(i => i.Kind == ItemKind.DriveRoot);
}

public static class SelectionAnalyzer
{
    /// <summary>
    /// Normalizes paths, removes duplicates (case-insensitive), and drops any path inside another
    /// selected path. Keeps first-seen order. Pure string logic; no disk access.
    /// </summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string> paths)
    {
        var unique = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var n = PathUtil.Normalize(raw);
            if (seen.Add(n))
                unique.Add(n);
        }
        return unique.Where(p => !unique.Any(other => PathUtil.IsStrictlyInside(p, other))).ToList();
    }

    public static SelectionAnalysis Analyze(IEnumerable<string> paths, IPathProbe probe)
    {
        var items = Normalize(paths).Select(p => new SelectedItem(p, Classify(p, probe))).ToList();
        return new SelectionAnalysis(items);
    }

    public static ItemKind Classify(string path, IPathProbe probe)
    {
        if (PathUtil.IsDriveRoot(path))
            return ItemKind.DriveRoot;
        return probe.DirectoryExists(path) ? ItemKind.Folder : ItemKind.File;
    }
}
