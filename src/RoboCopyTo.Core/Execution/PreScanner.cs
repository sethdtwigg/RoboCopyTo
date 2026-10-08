using System.Globalization;
using System.IO.Enumeration;
using RoboCopyTo.Core.Options;
using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.Core.Execution;

public readonly record struct ScanTotals(long Files, long Bytes)
{
    public static ScanTotals operator +(ScanTotals a, ScanTotals b) => new(a.Files + b.Files, a.Bytes + b.Bytes);
}

/// <summary>
/// Counts the files and bytes each job's source holds, so progress can be shown against a total.
/// Applies what affects which files robocopy visits: subfolders, /XF, /XD, /XJ (/XJD, /XJF) and /LEV:n,
/// from the options and from Extra arguments. Like robocopy without /XJ, it follows junctions and
/// symbolic links, but never enters the same directory twice (avoiding junction loops).
/// </summary>
public static class PreScanner
{
    private const int ReportEvery = 200;

    /// <summary>The scan-relevant rules, derived from options plus Extra arguments.</summary>
    internal sealed record ScanRules(
        IReadOnlyList<string> ExcludeFiles,
        IReadOnlyList<string> ExcludeDirs,
        bool Recurse,
        int? MaxLevels,
        bool SkipLinkedDirs,
        bool SkipLinkedFiles)
    {
        public static ScanRules From(RobocopyOptions o)
        {
            var xf = new List<string>(CommandBuilder.ExcludePatterns(o.ExcludeFiles));
            var xd = new List<string>(CommandBuilder.ExcludePatterns(o.ExcludeFolders));
            var recurse = o.EffectiveIncludeSubfolders;
            int? levels = null;
            bool xjd = o.SkipJunctions, xjf = o.SkipJunctions;

            // Extra arguments: /XF and /XD take every following value up to the next switch.
            List<string>? collecting = null;
            foreach (var token in ArgumentTokenizer.Split(o.ExtraArguments))
            {
                var n = ArgumentTokenizer.NormalizeSwitch(token);
                if (n.StartsWith('/'))
                {
                    collecting = null;
                    switch (n)
                    {
                        case "/XF": collecting = xf; break;
                        case "/XD": collecting = xd; break;
                        case "/XJ": xjd = xjf = true; break;
                        case "/XJD": xjd = true; break;
                        case "/XJF": xjf = true; break;
                        case "/E" or "/S" or "/MIR": recurse = true; break;
                    }
                    if (n.StartsWith("/LEV:", StringComparison.Ordinal) && int.TryParse(n[5..], NumberStyles.None, CultureInfo.InvariantCulture, out var lev) && lev > 0)
                        levels = lev;
                    continue;
                }
                collecting?.Add(token.Replace("\"", "").TrimEnd('\\'));
            }
            return new ScanRules(xf, xd, recurse, levels, xjd, xjf);
        }
    }

    /// <returns>One total per job, in job order.</returns>
    public static IReadOnlyList<ScanTotals> Scan(
        IReadOnlyList<RobocopyJob> jobs,
        RobocopyOptions options,
        IProgress<ScanTotals>? progress = null,
        CancellationToken ct = default)
    {
        var rules = ScanRules.From(options);
        var running = new ScanTotals();
        var results = new List<ScanTotals>(jobs.Count);
        var sinceReport = 0;

        void Count(long bytes)
        {
            running += new ScanTotals(1, bytes);
            if (++sinceReport >= ReportEvery)
            {
                sinceReport = 0;
                progress?.Report(running);
            }
        }

        foreach (var job in jobs)
        {
            ct.ThrowIfCancellationRequested();
            var before = running;
            if (job.Kind == JobKind.Files)
            {
                foreach (var name in job.Files)
                {
                    var info = new FileInfo(Path.Combine(job.Source, name));
                    if (info.Exists && IncludeFile(info, rules))
                        Count(info.Length);
                }
            }
            else if (Directory.Exists(job.Source))
            {
                ScanFolder(new DirectoryInfo(job.Source), rules, Count, ct);
            }
            results.Add(new ScanTotals(running.Files - before.Files, running.Bytes - before.Bytes));
        }

        progress?.Report(running);
        return results;
    }

    private static void ScanFolder(DirectoryInfo root, ScanRules rules, Action<long> count, CancellationToken ct)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<(DirectoryInfo Dir, int Level)>();
        pending.Push((root, 1));
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, level) = pending.Pop();
            if (!visited.Add(RealPath(dir)))
                continue;
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = dir.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue; // robocopy reports these itself
            }
            foreach (var entry in entries)
            {
                if (entry is FileInfo file)
                {
                    if (IncludeFile(file, rules))
                        count(file.Length);
                }
                else if (entry is DirectoryInfo sub && rules.Recurse && (rules.MaxLevels is null || level < rules.MaxLevels))
                {
                    var isLink = sub.Attributes.HasFlag(FileAttributes.ReparsePoint);
                    if (isLink && rules.SkipLinkedDirs)
                        continue;
                    if (Matches(rules.ExcludeDirs, sub.Name, sub.FullName))
                        continue;
                    pending.Push((sub, level + 1));
                }
            }
        }
    }

    private static bool IncludeFile(FileInfo file, ScanRules rules)
    {
        if (rules.SkipLinkedFiles && file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return false;
        return !Matches(rules.ExcludeFiles, file.Name, file.FullName);
    }

    /// <summary>The directory's final target, so a junction pointing back up the tree is entered only once.</summary>
    private static string RealPath(DirectoryInfo dir)
    {
        try
        {
            if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint) && dir.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                return Path.GetFullPath(target.FullName).TrimEnd('\\');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return dir.FullName.TrimEnd('\\');
    }

    /// <summary>Robocopy-style match: a pattern with a backslash matches the full path, otherwise the name.</summary>
    internal static bool Matches(IReadOnlyList<string> patterns, string name, string fullPath)
    {
        foreach (var p in patterns)
        {
            var target = p.Contains('\\') ? fullPath : name;
            if (FileSystemName.MatchesSimpleExpression(p.TrimEnd('\\'), target.TrimEnd('\\'), ignoreCase: true))
                return true;
        }
        return false;
    }
}
