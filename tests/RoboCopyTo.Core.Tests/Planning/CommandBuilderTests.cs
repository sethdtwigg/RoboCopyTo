using RoboCopyTo.Core.Options;
using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.Core.Tests.Planning;

public class CommandBuilderTests
{
    private const string Log = @"C:\Logs\run.log";
    private const string AppSwitches = "/BYTES /FP /NP /TEE /UNILOG+:\"C:\\Logs\\run.log\"";
    private static readonly BuildContext Ctx = new(Log);

    private static SelectedItem Folder(string p) => new(p, ItemKind.Folder);
    private static SelectedItem File(string p) => new(p, ItemKind.File);

    private static IReadOnlyList<RobocopyJob> Build(RobocopyOptions o, BuildContext? ctx = null, params SelectedItem[] items)
        => CommandBuilder.Build(items, @"D:\Backup", o, ctx ?? Ctx);

    [Fact]
    public void Folder_selection_targets_dest_plus_folder_name()
    {
        var job = Assert.Single(Build(new RobocopyOptions(), null, Folder(@"C:\Data\Photos")));
        Assert.Equal(JobKind.Folder, job.Kind);
        Assert.Equal(@"C:\Data\Photos", job.Source);
        Assert.Equal(@"D:\Backup\Photos", job.Destination);
        Assert.StartsWith("\"C:\\Data\\Photos\" \"D:\\Backup\\Photos\" /E ", job.Arguments);
    }

    [Fact]
    public void Each_folder_becomes_its_own_job()
    {
        var jobs = Build(new RobocopyOptions(), null, Folder(@"C:\A"), Folder(@"C:\B"));
        Assert.Equal([@"D:\Backup\A", @"D:\Backup\B"], jobs.Select(j => j.Destination));
    }

    [Fact]
    public void Default_options_emit_switches_in_spec_order()
    {
        var job = Assert.Single(Build(new RobocopyOptions(), null, Folder(@"C:\A")));
        Assert.Equal($"\"C:\\A\" \"D:\\Backup\\A\" /E /COPY:DAT /DCOPY:DAT /MT:8 /R:3 /W:5 {AppSwitches}", job.Arguments);
        Assert.Equal("robocopy " + job.Arguments, job.CommandLine);
    }

    [Fact]
    public void Every_switch_enabled_emits_in_spec_order()
    {
        var o = new RobocopyOptions
        {
            Mirror = true,
            CopyPermissions = true,
            CopyOwner = true,
            CopyAuditing = true,
            RestartableBackup = true,
            BackupMode = true,
            Unbuffered = true,
            Threads = 16,
            ExistingFiles = ExistingFileMode.SkipExisting,
            ExcludeFiles = "*.tmp \"thumbs db\"",
            ExcludeFolders = "node_modules .git",
            SkipJunctions = true,
            NetworkFriendly = true,
            ExtraArguments = "/V /NDL",
        };
        var job = Assert.Single(Build(o, new BuildContext(Log, DryRun: true, DestinationIsFat: true), Folder(@"C:\A")));
        Assert.Equal(
            "\"C:\\A\" \"D:\\Backup\\A\" /MIR /COPY:DATSOU /DCOPY:DAT /ZB /B /J /MT:16 /XC /XN /XO /FFT " +
            "/XF *.tmp \"thumbs db\" /XD node_modules .git /XJ /R:10 /W:15 /TBD " + AppSwitches + " /L /V /NDL",
            job.Arguments);
    }

    [Fact]
    public void Restartable_without_backup_emits_Z()
    {
        var o = new RobocopyOptions { Restartable = true, Multithreaded = false };
        var job = Assert.Single(Build(o, null, Folder(@"C:\A")));
        Assert.Contains(" /DCOPY:DAT /Z /R:3 ", job.Arguments);
    }

    [Fact]
    public void Restartable_backup_replaces_Z()
    {
        var o = new RobocopyOptions { Restartable = true, RestartableBackup = true };
        var job = Assert.Single(Build(o, null, Folder(@"C:\A")));
        Assert.Contains(" /ZB ", job.Arguments);
        Assert.DoesNotContain(" /Z ", job.Arguments);
    }

    [Theory]
    [InlineData(ExistingFileMode.CopyIfDifferent, "/MT:8 /R:3")]
    [InlineData(ExistingFileMode.OnlyNewer, "/MT:8 /XO /R:3")]
    [InlineData(ExistingFileMode.SkipExisting, "/MT:8 /XC /XN /XO /R:3")]
    [InlineData(ExistingFileMode.AlwaysOverwrite, "/MT:8 /IS /IT /IM /R:3")]
    public void Existing_file_modes(ExistingFileMode mode, string expected)
    {
        var job = Assert.Single(Build(new RobocopyOptions { ExistingFiles = mode }, null, Folder(@"C:\A")));
        Assert.Contains(expected, job.Arguments);
    }

    [Fact]
    public void Fat_destination_adds_FFT_after_existing_mode()
    {
        var job = Assert.Single(Build(new RobocopyOptions { ExistingFiles = ExistingFileMode.OnlyNewer }, new BuildContext(Log, DestinationIsFat: true), Folder(@"C:\A")));
        Assert.Contains("/MT:8 /XO /FFT /R:3", job.Arguments);
    }

    [Fact]
    public void Subfolders_off_emits_no_scope_switch()
    {
        var job = Assert.Single(Build(new RobocopyOptions { IncludeSubfolders = false }, null, Folder(@"C:\A")));
        Assert.StartsWith("\"C:\\A\" \"D:\\Backup\\A\" /COPY:DAT", job.Arguments);
    }

    [Fact]
    public void Mirror_replaces_E_even_if_subfolders_flag_is_off()
    {
        var job = Assert.Single(Build(new RobocopyOptions { Mirror = true, IncludeSubfolders = false }, null, Folder(@"C:\A")));
        Assert.Contains("\" /MIR /COPY:DAT", job.Arguments);
        Assert.DoesNotContain("/E ", job.Arguments);
    }

    [Fact]
    public void Folder_timestamps_off_omits_DCOPY()
    {
        var job = Assert.Single(Build(new RobocopyOptions { KeepFolderTimestamps = false }, null, Folder(@"C:\A")));
        Assert.DoesNotContain("/DCOPY", job.Arguments);
    }

    [Fact]
    public void Multithreading_off_omits_MT()
    {
        var job = Assert.Single(Build(new RobocopyOptions { Multithreaded = false }, null, Folder(@"C:\A")));
        Assert.DoesNotContain("/MT", job.Arguments);
    }

    [Fact]
    public void Network_friendly_overrides_retry_values()
    {
        var job = Assert.Single(Build(new RobocopyOptions { RetryCount = 1, RetryWaitSeconds = 2, NetworkFriendly = true }, null, Folder(@"C:\A")));
        Assert.Contains("/R:10 /W:15 /TBD", job.Arguments);
    }

    [Fact]
    public void Custom_retry_values()
    {
        var job = Assert.Single(Build(new RobocopyOptions { RetryCount = 0, RetryWaitSeconds = 30 }, null, Folder(@"C:\A")));
        Assert.Contains("/R:0 /W:30 /BYTES", job.Arguments);
    }

    [Fact]
    public void Dry_run_appends_L_before_extra_arguments()
    {
        var job = Assert.Single(Build(new RobocopyOptions { ExtraArguments = "/NDL" }, new BuildContext(Log, DryRun: true), Folder(@"C:\A")));
        Assert.EndsWith($"{AppSwitches} /L /NDL", job.Arguments);
    }

    [Fact]
    public void Loose_files_are_grouped_by_parent()
    {
        var jobs = Build(new RobocopyOptions(), null,
            File(@"C:\Docs\a.txt"), File(@"C:\Pics\x.jpg"), File(@"C:\Docs\b c.txt"));
        Assert.Equal(2, jobs.Count);

        Assert.Equal(JobKind.Files, jobs[0].Kind);
        Assert.Equal(@"C:\Docs", jobs[0].Source);
        Assert.Equal(@"D:\Backup", jobs[0].Destination);
        Assert.Equal(["a.txt", "b c.txt"], jobs[0].Files);
        Assert.StartsWith("\"C:\\Docs\" \"D:\\Backup\" \"a.txt\" \"b c.txt\" /COPY:DAT", jobs[0].Arguments);

        Assert.Equal(@"C:\Pics", jobs[1].Source);
        Assert.Equal(["x.jpg"], jobs[1].Files);
    }

    [Fact]
    public void Folders_come_before_file_groups_in_selection_order()
    {
        var jobs = Build(new RobocopyOptions(), null, File(@"C:\Docs\a.txt"), Folder(@"C:\A"));
        Assert.Equal([JobKind.Folder, JobKind.Files], jobs.Select(j => j.Kind));
    }

    [Theory]
    [InlineData(true, false, "")]
    [InlineData(false, true, "")]
    [InlineData(false, false, "/E /S /MIR /PURGE /NDL")]
    public void Loose_file_jobs_never_get_E_S_MIR_or_PURGE(bool mirror, bool subfolders, string extra)
    {
        var o = new RobocopyOptions { Mirror = mirror, IncludeSubfolders = subfolders, ExtraArguments = extra };
        var job = Assert.Single(Build(o, null, File(@"C:\Docs\a.txt")));
        var tokens = ArgumentTokenizer.Split(job.Arguments).Select(t => t.ToUpperInvariant()).ToList();
        Assert.DoesNotContain("/E", tokens);
        Assert.DoesNotContain("/S", tokens);
        Assert.DoesNotContain("/MIR", tokens);
        Assert.DoesNotContain("/PURGE", tokens);
    }

    [Fact]
    public void Loose_file_jobs_keep_other_extra_arguments()
    {
        var job = Assert.Single(Build(new RobocopyOptions { ExtraArguments = "/E /NDL" }, null, File(@"C:\Docs\a.txt")));
        Assert.EndsWith($"{AppSwitches} /NDL", job.Arguments);
    }

    [Fact]
    public void Folder_jobs_keep_extra_arguments_verbatim()
    {
        var job = Assert.Single(Build(new RobocopyOptions { ExtraArguments = "  /PURGE   /XA:H " }, null, Folder(@"C:\A")));
        Assert.EndsWith($"{AppSwitches} /PURGE   /XA:H", job.Arguments);
    }

    [Fact]
    public void Long_file_lists_split_below_30000_characters()
    {
        var files = Enumerable.Range(0, 2000).Select(i => File($@"C:\Docs\file-with-a-fairly-long-name-number-{i:D5}.txt")).ToArray();
        var jobs = Build(new RobocopyOptions(), null, files);

        Assert.True(jobs.Count > 1);
        Assert.All(jobs, j => Assert.True(j.CommandLine.Length < CommandBuilder.MaxCommandLineLength, $"length {j.CommandLine.Length}"));
        Assert.Equal(files.Select(f => Path.GetFileName(f.Path)), jobs.SelectMany(j => j.Files));
        Assert.All(jobs, j => Assert.Equal(@"C:\Docs", j.Source));
    }

    [Fact]
    public void Drive_root_sources_produce_no_job()
    {
        Assert.Empty(Build(new RobocopyOptions(), null, new SelectedItem(@"E:\", ItemKind.DriveRoot)));
    }

    [Fact]
    public void Root_destination_is_quoted_with_doubled_backslash_for_loose_files()
    {
        var job = Assert.Single(CommandBuilder.Build([File(@"C:\Docs\a.txt")], @"E:\", new RobocopyOptions(), Ctx));
        Assert.StartsWith("\"C:\\Docs\" \"E:\\\\\" \"a.txt\"", job.Arguments);
        Assert.Equal(@"E:\", job.Destination);
    }

    [Fact]
    public void Root_destination_for_folder_job()
    {
        var job = Assert.Single(CommandBuilder.Build([Folder(@"C:\A")], @"E:\", new RobocopyOptions(), Ctx));
        Assert.StartsWith("\"C:\\A\" \"E:\\A\"", job.Arguments);
    }

    [Fact]
    public void Unc_share_destination_and_source()
    {
        var job = Assert.Single(CommandBuilder.Build([Folder(@"\\nas\media")], @"\\backup\share\", new RobocopyOptions(), Ctx));
        Assert.StartsWith("\"\\\\nas\\media\" \"\\\\backup\\share\\media\"", job.Arguments);
    }

    [Fact]
    public void Segments_mark_app_required_switches()
    {
        var job = Assert.Single(Build(new RobocopyOptions(), new BuildContext(Log, DryRun: true), Folder(@"C:\A")));
        var app = string.Join(" ", job.Segments.Where(s => s.IsAppRequired).Select(s => s.Text));
        Assert.Equal(AppSwitches, app);
        Assert.Equal(job.Arguments, string.Join(" ", job.Segments.Select(s => s.Text)));
    }

    [Fact]
    public void Mirror_job_records_mirror_flag()
    {
        var jobs = Build(new RobocopyOptions { Mirror = true }, null, Folder(@"C:\A"));
        Assert.True(Assert.Single(jobs).IsMirror);
    }

    [Fact]
    public void Purge_in_extra_arguments_counts_as_mirror_for_folder_jobs()
    {
        var jobs = Build(new RobocopyOptions { ExtraArguments = "/purge" }, null, Folder(@"C:\A"));
        Assert.True(Assert.Single(jobs).DeletesExtras);
    }
}
