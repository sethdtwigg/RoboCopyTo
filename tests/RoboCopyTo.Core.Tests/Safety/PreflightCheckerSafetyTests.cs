using RoboCopyTo.Core.Options;
using RoboCopyTo.Core.Planning;
using RoboCopyTo.Core.Safety;

namespace RoboCopyTo.Core.Tests.Safety;

/// <summary>Cases found in review: switch spellings robocopy accepts, nested targets, collisions, aliases, length.</summary>
public class PreflightCheckerSafetyTests
{
    private sealed class AllFolders : IPathProbe
    {
        public bool DirectoryExists(string path) => true;
        public bool FileExists(string path) => false;
    }

    private static SelectedItem Folder(string p) => new(p, ItemKind.Folder);
    private static SelectedItem File(string p) => new(p, ItemKind.File);

    private static PreflightResult Check(SelectedItem[] items, string dest = @"D:\Backup", RobocopyOptions? o = null,
        IReadOnlyList<RobocopyJob>? jobs = null, Func<string, string>? canon = null)
        => PreflightChecker.Check(new PreflightInput(items, dest, o ?? new RobocopyOptions(), new AllFolders(), Jobs: jobs, Canonicalize: canon));

    private static PreflightIssue? Issue(PreflightResult r, string code) => r.Issues.FirstOrDefault(i => i.Code == code);

    [Theory]
    [InlineData("/MIR")]
    [InlineData("\"/MIR\"")]
    [InlineData("-MIR")]
    [InlineData("-mir")]
    [InlineData("-Purge")]
    [InlineData("\"-PURGE\"")]
    public void Every_spelling_of_mirror_or_purge_blocks_with_loose_files(string extra)
    {
        var r = Check([File(@"C:\Docs\a.txt")], o: new RobocopyOptions { ExtraArguments = extra });
        Assert.Equal(PreflightSeverity.Block, Issue(r, PreflightCodes.MirrorWithLooseFiles)?.Severity);
    }

    [Theory]
    [InlineData("-MIR")]
    [InlineData("\"/purge\"")]
    public void Every_spelling_of_mirror_requires_deletion_confirmation(string extra)
    {
        var r = Check([Folder(@"C:\A")], o: new RobocopyOptions { ExtraArguments = extra });
        Assert.Equal(PreflightSeverity.Confirm, Issue(r, PreflightCodes.MirrorDeletes)?.Severity);
    }

    [Theory]
    [InlineData("-MOV")]
    [InlineData("-move")]
    [InlineData("\"/MOVE\"")]
    public void Every_spelling_of_move_requires_confirmation(string extra)
    {
        var r = Check([Folder(@"C:\A")], o: new RobocopyOptions { ExtraArguments = extra });
        Assert.Equal(PreflightSeverity.Confirm, Issue(r, PreflightCodes.MoveDeletesSource)?.Severity);
    }

    [Theory]
    [InlineData("-MIR", true)]
    [InlineData("\"/PURGE\"", true)]
    [InlineData("-e", false)]
    [InlineData("\"/S\"", false)]
    public void Loose_file_jobs_strip_every_spelling_of_folder_only_switches(string extra, bool isMirror)
    {
        var job = Assert.Single(CommandBuilder.Build([File(@"C:\Docs\a.txt")], @"D:\B", new RobocopyOptions { ExtraArguments = extra + " /NDL" }, new BuildContext(@"C:\l.log")));
        Assert.EndsWith(" /NDL", job.Arguments);
        Assert.DoesNotContain(extra, job.Arguments);
        Assert.False(job.DeletesExtras);
        _ = isMirror;
    }

    [Fact]
    public void Folder_job_with_dash_mir_in_extra_arguments_deletes_extras()
    {
        var job = Assert.Single(CommandBuilder.Build([Folder(@"C:\A")], @"D:\B", new RobocopyOptions { ExtraArguments = "-mir" }, new BuildContext(@"C:\l.log")));
        Assert.True(job.DeletesExtras);
    }

    [Fact]
    public void Mirror_whose_target_contains_the_source_is_blocked()
    {
        // C:\Proj\App\App → C:\Proj lands at C:\Proj\App, which contains the source; /MIR would purge it.
        var r = Check([Folder(@"C:\Proj\App\App")], dest: @"C:\Proj", o: new RobocopyOptions { Mirror = true });
        Assert.Equal(PreflightSeverity.Block, Issue(r, PreflightCodes.SourceInsideTarget)?.Severity);
    }

    [Fact]
    public void Plain_copy_whose_target_contains_the_source_is_allowed()
    {
        var r = Check([Folder(@"C:\Proj\App\App")], dest: @"C:\Proj");
        Assert.Null(Issue(r, PreflightCodes.SourceInsideTarget));
        Assert.False(r.IsBlocked);
    }

    [Fact]
    public void Same_named_folders_are_blocked_with_mirror()
    {
        var r = Check([Folder(@"C:\a\Data"), Folder(@"D:\b\data")], dest: @"E:\Backup", o: new RobocopyOptions { Mirror = true });
        var issue = Issue(r, PreflightCodes.TargetCollision);
        Assert.Equal(PreflightSeverity.Block, issue?.Severity);
        Assert.Equal([@"C:\a\Data", @"D:\b\data"], issue!.Details);
        Assert.Single(Issue(r, PreflightCodes.MirrorDeletes)!.Details);
    }

    [Fact]
    public void Same_named_folders_warn_without_mirror()
    {
        var r = Check([Folder(@"C:\a\Data"), Folder(@"D:\b\Data")], dest: @"E:\Backup");
        Assert.Equal(PreflightSeverity.Warn, Issue(r, PreflightCodes.TargetCollision)?.Severity);
        Assert.False(r.IsBlocked);
    }

    [Fact]
    public void Same_named_loose_files_from_different_folders_warn()
    {
        var r = Check([File(@"C:\a\report.txt"), File(@"C:\b\REPORT.txt")]);
        Assert.Equal(PreflightSeverity.Warn, Issue(r, PreflightCodes.TargetCollision)?.Severity);
    }

    [Fact]
    public void Distinct_names_do_not_collide()
    {
        var r = Check([Folder(@"C:\a\Data"), Folder(@"C:\a\Docs"), File(@"C:\x\a.txt"), File(@"C:\y\b.txt")]);
        Assert.Null(Issue(r, PreflightCodes.TargetCollision));
    }

    [Fact]
    public void Mapped_drive_alias_of_the_source_is_recognised()
    {
        static string Canon(string p) => p.StartsWith("Z:", StringComparison.OrdinalIgnoreCase) ? @"\\srv\data" + p[2..] : p;
        var r = Check([Folder(@"\\srv\data\Projects")], dest: @"Z:\Projects\backup", canon: Canon);
        Assert.Equal(PreflightSeverity.Block, Issue(r, PreflightCodes.DestinationInsideSource)?.Severity);
    }

    [Fact]
    public void Long_path_prefix_is_recognised_as_the_same_folder()
    {
        var r = Check([Folder(@"C:\A")], dest: @"\\?\C:\A\inner");
        Assert.Equal(PreflightSeverity.Block, Issue(r, PreflightCodes.DestinationInsideSource)?.Severity);
    }

    [Fact]
    public void Command_longer_than_windows_allows_is_blocked()
    {
        var o = new RobocopyOptions { ExcludeFolders = string.Join(" ", Enumerable.Range(0, 3000).Select(i => $"folder{i:D5}")) };
        var jobs = CommandBuilder.Build([Folder(@"C:\A")], @"D:\B", o, new BuildContext(@"C:\l.log"));
        Assert.True(jobs[0].CommandLine.Length > PreflightChecker.MaxProcessCommandLine);

        var r = Check([Folder(@"C:\A")], o: o, jobs: jobs);
        Assert.Equal(PreflightSeverity.Block, Issue(r, PreflightCodes.CommandTooLong)?.Severity);
    }

    [Fact]
    public void Normal_command_length_passes()
    {
        var jobs = CommandBuilder.Build([Folder(@"C:\A")], @"D:\B", new RobocopyOptions(), new BuildContext(@"C:\l.log"));
        Assert.Null(Issue(Check([Folder(@"C:\A")], jobs: jobs), PreflightCodes.CommandTooLong));
    }

    [Fact]
    public void File_split_accounts_for_a_large_fixed_part()
    {
        var o = new RobocopyOptions { ExcludeFiles = string.Join(" ", Enumerable.Range(0, 2000).Select(i => $"x{i:D4}.tmp")) };
        var files = Enumerable.Range(0, 3000).Select(i => File($@"C:\Docs\file-number-{i:D5}.txt")).ToArray();
        var jobs = CommandBuilder.Build(files, @"D:\B", o, new BuildContext(@"C:\l.log"));
        Assert.True(jobs.Count > 1);
        Assert.All(jobs, j => Assert.True(j.CommandLine.Length < CommandBuilder.MaxCommandLineLength));
        Assert.Equal(3000, jobs.Sum(j => j.Files.Count));
    }
}
