using RoboCopyTo.Core.Options;

namespace RoboCopyTo.Core.Planning;

/// <param name="LogPath">The run's log file, passed to /UNILOG+.</param>
/// <param name="DryRun">Adds /L.</param>
/// <param name="DestinationIsFat">FAT32/exFAT destination; adds /FFT.</param>
public sealed record BuildContext(string LogPath, bool DryRun = false, bool DestinationIsFat = false);

/// <summary>Turns a selection, a destination, and options into robocopy calls.</summary>
public static class CommandBuilder
{
    /// <summary>Loose-file jobs are split so each command line stays below this length.</summary>
    public const int MaxCommandLineLength = 30_000;

    /// <summary>Switches that loose-file jobs must never receive, even via Extra arguments.</summary>
    private static readonly string[] FolderOnlySwitches = ["/E", "/S", "/MIR", "/PURGE"];

    public static IReadOnlyList<RobocopyJob> Build(
        IReadOnlyList<SelectedItem> items,
        string destination,
        RobocopyOptions options,
        BuildContext context)
    {
        var dest = PathUtil.Normalize(destination);
        var jobs = new List<RobocopyJob>();

        foreach (var folder in items.Where(i => i.Kind == ItemKind.Folder))
            jobs.Add(BuildFolderJob(folder.Path, dest, options, context));

        var groups = items
            .Where(i => i.Kind == ItemKind.File)
            .GroupBy(i => PathUtil.Normalize(Path.GetDirectoryName(PathUtil.Normalize(i.Path))!), StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var names = group.Select(i => Path.GetFileName(PathUtil.Normalize(i.Path))).ToList();
            jobs.AddRange(BuildFileJobs(group.Key, names, dest, options, context));
        }

        return jobs;
    }

    private static RobocopyJob BuildFolderJob(string folder, string dest, RobocopyOptions o, BuildContext ctx)
    {
        var source = PathUtil.Normalize(folder);
        var target = Path.Combine(dest, PathUtil.GetFolderName(source));
        var segments = new List<ArgumentSegment> { new(PathUtil.Quote(source)), new(PathUtil.Quote(target)) };

        if (o.Mirror)
            segments.Add(new("/MIR"));
        else if (o.IncludeSubfolders)
            segments.Add(new("/E"));

        AddCommonSwitches(segments, o, ctx);

        var extra = (o.ExtraArguments ?? "").Trim();
        if (extra.Length > 0)
            segments.Add(new(extra));

        var deletesExtras = o.Mirror || ArgumentTokenizer.ContainsSwitch(o.ExtraArguments, "/MIR", "/PURGE");
        return new RobocopyJob(JobKind.Folder, source, target, [], segments, o.Mirror, deletesExtras, ctx.DryRun, ctx.LogPath);
    }

    private static IEnumerable<RobocopyJob> BuildFileJobs(string parent, List<string> names, string dest, RobocopyOptions o, BuildContext ctx)
    {
        var head = new List<ArgumentSegment> { new(PathUtil.Quote(parent)), new(PathUtil.Quote(dest)) };
        var tail = new List<ArgumentSegment>();
        AddCommonSwitches(tail, o, ctx);
        var extra = string.Join(" ", ArgumentTokenizer.Split(o.ExtraArguments)
            .Where(t => !ArgumentTokenizer.IsSwitch(t, FolderOnlySwitches)));
        if (extra.Length > 0)
            tail.Add(new(extra));

        // "robocopy " + head + " " + files... + " " + tail
        var fixedLength = RobocopyJob.Executable.Length + 1
            + head.Sum(s => s.Text.Length + 1)
            + tail.Sum(s => s.Text.Length + 1);

        var batch = new List<string>();
        var length = fixedLength;
        foreach (var name in names)
        {
            var added = name.Length + 3; // quotes + separating space
            if (batch.Count > 0 && length + added >= MaxCommandLineLength)
            {
                yield return MakeFileJob(parent, dest, batch, head, tail, ctx);
                batch = [];
                length = fixedLength;
            }
            batch.Add(name);
            length += added;
        }
        if (batch.Count > 0)
            yield return MakeFileJob(parent, dest, batch, head, tail, ctx);
    }

    private static RobocopyJob MakeFileJob(string parent, string dest, List<string> files, List<ArgumentSegment> head, List<ArgumentSegment> tail, BuildContext ctx)
    {
        var segments = new List<ArgumentSegment>(head);
        segments.AddRange(files.Select(f => new ArgumentSegment("\"" + f + "\"")));
        segments.AddRange(tail);
        return new RobocopyJob(JobKind.Files, parent, dest, files, segments, IsMirror: false, DeletesExtras: false, ctx.DryRun, ctx.LogPath);
    }

    /// <summary>Everything after the copy scope, in spec order, excluding Extra arguments.</summary>
    private static void AddCommonSwitches(List<ArgumentSegment> s, RobocopyOptions o, BuildContext ctx)
    {
        // 2. What to copy
        s.Add(new("/COPY:" + o.CopyFlags));
        if (o.KeepFolderTimestamps)
            s.Add(new("/DCOPY:DAT"));

        // 3. Copy mode
        if (o.RestartableBackup)
            s.Add(new("/ZB"));
        else if (o.Restartable)
            s.Add(new("/Z"));
        if (o.BackupMode)
            s.Add(new("/B"));
        if (o.Unbuffered)
            s.Add(new("/J"));
        if (o.Multithreaded)
            s.Add(new("/MT:" + o.EffectiveThreads));

        // 4. Existing files, then /FFT
        foreach (var sw in o.ExistingFiles.Switches())
            s.Add(new(sw));
        if (ctx.DestinationIsFat)
            s.Add(new("/FFT"));

        // 5. Filters
        var xf = ExcludePatterns(o.ExcludeFiles);
        if (xf.Count > 0)
            s.Add(new("/XF " + string.Join(" ", xf.Select(PathUtil.QuoteIfNeeded))));
        var xd = ExcludePatterns(o.ExcludeFolders);
        if (xd.Count > 0)
            s.Add(new("/XD " + string.Join(" ", xd.Select(PathUtil.QuoteIfNeeded))));
        if (o.SkipJunctions)
            s.Add(new("/XJ"));

        // 6. Retries
        var r = o.EffectiveRetries;
        s.Add(new($"/R:{r.Count}"));
        s.Add(new($"/W:{r.WaitSeconds}"));
        if (o.NetworkFriendly)
            s.Add(new("/TBD"));

        // 7. App-required
        foreach (var sw in AppRequiredSwitches(ctx.LogPath))
            s.Add(new(sw, IsAppRequired: true));

        // 8. Dry run
        if (ctx.DryRun)
            s.Add(new("/L"));
    }

    /// <summary>
    /// Exclude patterns as emitted. Trailing backslashes are removed (a quoted "C:\My Dir\" would escape its
    /// closing quote), except on a bare drive root, which never needs quoting.
    /// </summary>
    public static IReadOnlyList<string> ExcludePatterns(string? text)
        => ArgumentTokenizer.SplitUnquoted(text)
            .Select(p => PathUtil.IsDriveRoot(p) ? p.TrimEnd('\\') + "\\" : p.TrimEnd('\\'))
            .Where(p => p.Length > 0)
            .ToList();

    public static IReadOnlyList<string> AppRequiredSwitches(string logPath)
        => ["/BYTES", "/FP", "/NP", "/TEE", "/UNILOG+:" + PathUtil.Quote(logPath)];
}
