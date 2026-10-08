using System.Diagnostics;
using RoboCopyTo.Core.Execution;
using RoboCopyTo.Core.Options;
using RoboCopyTo.Core.Planning;
using RoboCopyTo.Core.Tests.Integration;

namespace RoboCopyTo.Core.Tests.Execution;

public sealed class PreScannerTests : IDisposable
{
    private readonly TempDir _t = new();

    public void Dispose() => _t.Dispose();

    private ScanTotals Scan(RobocopyOptions o, params SelectedItem[] items)
    {
        var jobs = CommandBuilder.Build(items, _t["dest"], o, new BuildContext(_t["l.log"]));
        var totals = PreScanner.Scan(jobs, o);
        return totals.Aggregate(new ScanTotals(), (a, b) => a + b);
    }

    private SelectedItem Folder(string rel) => new(_t[rel], ItemKind.Folder);

    private void MakeTree()
    {
        _t.File(@"src\a.txt", "12345");             // 5
        _t.File(@"src\b.tmp", "123");               // 3
        _t.File(@"src\sub\c.txt", "1234567");       // 7
        _t.File(@"src\sub\deeper\d.txt", "12");     // 2
        _t.File(@"src\node_modules\e.js", "1");     // 1
        _t.File(@"src\My Stuff\f.txt", "1234");     // 4
        var hidden = _t.File(@"src\hidden.txt", "123456"); // 6
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);
    }

    [Fact]
    public void Counts_every_file_including_hidden_and_system()
    {
        MakeTree();
        Assert.Equal(new ScanTotals(7, 28), Scan(new RobocopyOptions(), Folder("src")));
    }

    [Fact]
    public void Without_subfolders_only_the_top_level_counts()
    {
        MakeTree();
        Assert.Equal(new ScanTotals(3, 14), Scan(new RobocopyOptions { IncludeSubfolders = false }, Folder("src")));
    }

    [Fact]
    public void Exclude_patterns_from_options_and_extra_arguments_are_applied()
    {
        MakeTree();
        var o = new RobocopyOptions { ExcludeFiles = "*.tmp", ExcludeFolders = "node_modules \"My Stuff\"", ExtraArguments = "/XF hidden.txt /XD deeper /NDL" };
        Assert.Equal(new ScanTotals(2, 12), Scan(o, Folder("src")));
    }

    [Fact]
    public void Lev_in_extra_arguments_limits_depth()
    {
        MakeTree();
        // /LEV:2 = the source folder plus one level of subfolders.
        Assert.Equal(new ScanTotals(6, 26), Scan(new RobocopyOptions { ExtraArguments = "/LEV:2" }, Folder("src")));
    }

    [Fact]
    public void Loose_files_count_their_own_sizes_and_honor_XF()
    {
        MakeTree();
        var o = new RobocopyOptions { ExcludeFiles = "*.tmp" };
        Assert.Equal(new ScanTotals(1, 5), Scan(o, new SelectedItem(_t[@"src\a.txt"], ItemKind.File), new SelectedItem(_t[@"src\b.tmp"], ItemKind.File)));
    }

    [Fact]
    public void Junctions_are_followed_without_xj_and_skipped_with_it_and_loops_end()
    {
        _t.File(@"src\a.txt", "12345");
        _t.File(@"elsewhere\linked.txt", "123");
        Junction(_t[@"src\link"], _t["elsewhere"]);
        Junction(_t[@"src\loop"], _t["src"]); // points back up the tree

        Assert.Equal(new ScanTotals(2, 8), Scan(new RobocopyOptions(), Folder("src")));
        Assert.Equal(new ScanTotals(1, 5), Scan(new RobocopyOptions { SkipJunctions = true }, Folder("src")));
        Assert.Equal(new ScanTotals(1, 5), Scan(new RobocopyOptions { ExtraArguments = "-XJD" }, Folder("src")));
    }

    [Fact]
    public void Cancellation_stops_the_scan()
    {
        MakeTree();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var jobs = CommandBuilder.Build([Folder("src")], _t["dest"], new RobocopyOptions(), new BuildContext(_t["l.log"]));
        Assert.Throws<OperationCanceledException>(() => PreScanner.Scan(jobs, new RobocopyOptions(), null, cts.Token));
    }

    [Fact]
    public void Missing_source_counts_as_empty()
        => Assert.Equal(new ScanTotals(0, 0), Scan(new RobocopyOptions(), Folder("nope")));

    private static void Junction(string link, string target)
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        Assert.True(Directory.Exists(link));
    }
}
