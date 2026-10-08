using RoboCopyTo.Core.Options;
using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.Core.Safety;

/// <param name="ScannedBytes">Total bytes from the pre-scan, if it has run.</param>
/// <param name="FreeBytes">Free space on the destination volume, if known.</param>
/// <param name="Jobs">The planned calls, if built; used to check command-line length.</param>
/// <param name="Canonicalize">
/// Maps a path to a canonical form before comparing sources and destination, so aliases such as a mapped
/// drive (Z:\x) and its UNC path (\\srv\share\x) are recognised as the same folder. Defaults to no mapping.
/// </param>
public sealed record PreflightInput(
    IReadOnlyList<SelectedItem> Items,
    string Destination,
    RobocopyOptions Options,
    IPathProbe Probe,
    long? ScannedBytes = null,
    long? FreeBytes = null,
    IReadOnlyList<RobocopyJob>? Jobs = null,
    Func<string, string>? Canonicalize = null);

/// <summary>Safety checks run before Start and Dry run.</summary>
public static class PreflightChecker
{
    private static readonly char[] InvalidPathChars = ['<', '>', '"', '|', '?', '*'];

    /// <summary>Windows limits a process command line to 32,767 characters; stay safely below it.</summary>
    public const int MaxProcessCommandLine = 32_000;

    public static PreflightResult Check(PreflightInput input)
    {
        var issues = new List<PreflightIssue>();
        var o = input.Options;
        var items = input.Items;
        var hasLooseFiles = items.Any(i => i.Kind == ItemKind.File);

        if (items.Count == 0)
            issues.Add(new(PreflightSeverity.Block, PreflightCodes.NothingSelected, "Nothing is selected to copy."));

        foreach (var root in items.Where(i => i.Kind == ItemKind.DriveRoot))
            issues.Add(new(PreflightSeverity.Block, PreflightCodes.DriveRootSource,
                $"{root.Path} is a whole drive. Copying a drive root is not supported; select the folders inside it instead."));

        var mirrorRequested = o.Mirror || ArgumentTokenizer.ContainsSwitch(o.ExtraArguments, "/MIR", "/PURGE");
        if (mirrorRequested && hasLooseFiles)
            issues.Add(new(PreflightSeverity.Block, PreflightCodes.MirrorWithLooseFiles,
                "Mirror (/MIR or /PURGE) cannot be used while individual files are selected. Remove the files or turn off Mirror."));

        var dest = ValidateDestination(input.Destination, input.Probe, out var destError);
        if (dest is null)
        {
            issues.Add(new(PreflightSeverity.Block, PreflightCodes.DestinationInvalid, destError!));
            return new PreflightResult(issues);
        }

        // Relationship checks compare canonical forms (mapped drive vs UNC); messages show what the user typed.
        var canon = input.Canonicalize ?? (p => p);
        string C(string p) => PathUtil.Normalize(canon(PathUtil.Normalize(p)));
        var destC = C(dest);

        foreach (var item in items)
        {
            if (item.Kind == ItemKind.DriveRoot)
                continue;
            var source = PathUtil.Normalize(item.Path);
            var sourceC = C(source);
            if (item.Kind == ItemKind.Folder)
            {
                var target = Path.Combine(dest, PathUtil.GetFolderName(source));
                var targetC = C(target);
                if (PathUtil.AreSame(destC, sourceC))
                    issues.Add(new(PreflightSeverity.Block, PreflightCodes.DestinationIsSource, $"The destination is the same folder as the source {source}."));
                else if (PathUtil.AreSame(targetC, sourceC))
                    issues.Add(new(PreflightSeverity.Block, PreflightCodes.DestinationIsSource,
                        $"{source} would be copied onto itself, because the destination is the folder that contains it."));
                else if (PathUtil.IsStrictlyInside(destC, sourceC))
                    issues.Add(new(PreflightSeverity.Block, PreflightCodes.DestinationInsideSource,
                        $"The destination is inside the source folder {source}. Robocopy would copy the folder into itself repeatedly."));
                else if (mirrorRequested && PathUtil.IsStrictlyInside(sourceC, targetC))
                    issues.Add(new(PreflightSeverity.Block, PreflightCodes.SourceInsideTarget,
                        $"{source} is inside its own mirror target {target}. Mirror would treat the source as an extra folder and delete it."));
            }
            else
            {
                var parentC = C(Path.GetDirectoryName(source)!);
                var parent = PathUtil.Normalize(Path.GetDirectoryName(source)!);
                if (PathUtil.AreSame(destC, sourceC) || PathUtil.AreSame(destC, parentC))
                    issues.Add(new(PreflightSeverity.Block, PreflightCodes.DestinationIsSource,
                        $"The destination is the folder {parent}, which already contains {Path.GetFileName(source)}."));
            }
        }

        AddCollisionIssues(issues, items, dest, mirrorRequested);

        if (input.Jobs?.FirstOrDefault(j => j.CommandLine.Length > MaxProcessCommandLine) is { } tooLong)
            issues.Add(new(PreflightSeverity.Block, PreflightCodes.CommandTooLong,
                $"The robocopy command for {tooLong.Source} is {tooLong.CommandLine.Length:N0} characters, more than Windows allows ({MaxProcessCommandLine:N0}). Shorten the exclude lists or Extra arguments."));

        if (mirrorRequested)
        {
            var folders = items.Where(i => i.Kind == ItemKind.Folder)
                .Select(i => Path.Combine(dest, PathUtil.GetFolderName(i.Path)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            issues.Add(new(PreflightSeverity.Confirm, PreflightCodes.MirrorDeletes,
                "Mirror deletes files and folders in these destination folders that do not exist in the source:", folders));
        }

        if (ArgumentTokenizer.ContainsSwitch(o.ExtraArguments, "/MOV", "/MOVE"))
            issues.Add(new(PreflightSeverity.Confirm, PreflightCodes.MoveDeletesSource,
                "Extra arguments contain /MOV or /MOVE: source files will be deleted after they are copied."));

        if (input.ScannedBytes is { } needed && input.FreeBytes is { } free && free < needed)
            issues.Add(new(PreflightSeverity.Warn, PreflightCodes.LowFreeSpace,
                $"The destination has {Format.Bytes(free)} free, but the selection totals {Format.Bytes(needed)}. The copy may run out of space."));

        if (!input.Probe.DirectoryExists(dest))
            issues.Add(new(PreflightSeverity.OfferCreate, PreflightCodes.DestinationMissing, $"The destination folder {dest} does not exist. Create it?"));

        return new PreflightResult(issues);
    }

    /// <summary>Items that would land on the same destination path.</summary>
    private static void AddCollisionIssues(List<PreflightIssue> issues, IReadOnlyList<SelectedItem> items, string dest, bool mirror)
    {
        var targets = items
            .Where(i => i.Kind is ItemKind.Folder or ItemKind.File)
            .Select(i => (Item: i, Target: Path.Combine(dest, i.Kind == ItemKind.Folder ? PathUtil.GetFolderName(i.Path) : Path.GetFileName(PathUtil.Normalize(i.Path)))))
            .GroupBy(t => t.Target, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();
        foreach (var g in targets)
        {
            var sources = g.Select(t => PathUtil.Normalize(t.Item.Path)).ToList();
            var anyFolder = g.Any(t => t.Item.Kind == ItemKind.Folder);
            if (mirror && anyFolder)
                issues.Add(new(PreflightSeverity.Block, PreflightCodes.TargetCollision,
                    $"These items would all be mirrored into {g.Key}, so each mirror would delete what the one before it copied. Rename one of them or copy them separately:", sources));
            else
                issues.Add(new(PreflightSeverity.Warn, PreflightCodes.TargetCollision,
                    $"These items would all land at {g.Key}; files with the same names will overwrite each other:", sources));
        }
    }

    /// <summary>Returns the normalized destination, or null with a reason.</summary>
    public static string? ValidateDestination(string? destination, IPathProbe probe, out string? error)
    {
        error = null;
        var d = PathUtil.StripLongPathPrefix(destination?.Trim() ?? "");
        if (d.Length == 0)
        {
            error = "Choose a destination folder.";
            return null;
        }
        if (d.IndexOfAny(InvalidPathChars) >= 0 || d.Any(char.IsControl) || (d.Length > 2 && d.IndexOf(':', 2) >= 0))
        {
            error = $"The destination \"{d}\" contains characters that are not allowed in a folder path.";
            return null;
        }
        var isDrivePath = d.Length >= 2 && char.IsAsciiLetter(d[0]) && d[1] == ':' && (d.Length == 2 || d[2] is '\\' or '/');
        var isUnc = d.StartsWith(@"\\", StringComparison.Ordinal) && d[2..].TrimEnd('\\').Split('\\').Length >= 2 && !d.StartsWith(@"\\?\", StringComparison.Ordinal);
        if (!isDrivePath && !isUnc)
        {
            error = $"The destination \"{d}\" must be a full path such as D:\\Backup or \\\\server\\share\\folder.";
            return null;
        }
        string normalized;
        try
        {
            normalized = PathUtil.Normalize(d);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"The destination \"{d}\" is not a valid path.";
            return null;
        }
        if (probe.FileExists(normalized))
        {
            error = $"The destination \"{normalized}\" is a file, not a folder.";
            return null;
        }
        return normalized;
    }
}
