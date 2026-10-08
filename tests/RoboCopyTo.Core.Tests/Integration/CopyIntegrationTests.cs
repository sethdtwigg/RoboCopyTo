using RoboCopyTo.Core.Options;
using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.Core.Tests.Integration;

/// <summary>Real robocopy against temp folders only.</summary>
[Collection("robocopy")]
public class CopyIntegrationTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly RobocopyOptions _fast = new() { RetryCount = 0, RetryWaitSeconds = 0 };

    public void Dispose() => _t.Dispose();

    private BuildContext Ctx(bool dryRun = false) => new(_t["run.log"], dryRun);

    private static SelectedItem Folder(string p) => new(p, ItemKind.Folder);
    private static SelectedItem File(string p) => new(p, ItemKind.File);

    private IReadOnlyList<Robo.Result> Copy(RobocopyOptions o, string dest, bool dryRun = false, params SelectedItem[] items)
        => Robo.RunAll(CommandBuilder.Build(items, dest, o, Ctx(dryRun)));

    [Fact]
    public void Folder_tree_lands_at_dest_plus_folder_name()
    {
        _t.File(@"src\Photos\a.jpg");
        _t.File(@"src\Photos\2024\b.jpg");
        var dest = _t.Dir("dest");

        var results = Copy(_fast, dest, false, Folder(_t[@"src\Photos"]));

        Assert.All(results, r => Assert.InRange(r.ExitCode, 0, 7));
        Assert.True(System.IO.File.Exists(_t[@"dest\Photos\a.jpg"]));
        Assert.True(System.IO.File.Exists(_t[@"dest\Photos\2024\b.jpg"]));
        Assert.False(System.IO.File.Exists(_t[@"dest\a.jpg"]));
    }

    [Fact]
    public void Loose_files_copy_only_the_selected_ones_not_same_named_files_in_subfolders()
    {
        _t.File(@"src\a.txt", "top");
        _t.File(@"src\b.txt", "top b");
        _t.File(@"src\sub\a.txt", "nested");
        _t.File(@"src\other.txt", "unselected");
        var dest = _t.Dir("dest");

        var results = Copy(_fast with { IncludeSubfolders = true }, dest, false, File(_t[@"src\a.txt"]), File(_t[@"src\b.txt"]));

        Assert.All(results, r => Assert.InRange(r.ExitCode, 0, 7));
        Assert.Equal("top", System.IO.File.ReadAllText(_t[@"dest\a.txt"]));
        Assert.True(System.IO.File.Exists(_t[@"dest\b.txt"]));
        Assert.False(Directory.Exists(_t[@"dest\sub"]));
        Assert.False(System.IO.File.Exists(_t[@"dest\other.txt"]));
    }

    [Fact]
    public void Mirror_removes_extras_only_inside_dest_folder_name()
    {
        _t.File(@"src\Docs\keep.txt");
        _t.File(@"dest\Docs\extra.txt");
        _t.File(@"dest\Docs\old\extra2.txt");
        _t.File(@"dest\unrelated.txt");
        _t.File(@"dest\OtherFolder\x.txt");

        var results = Copy(_fast with { Mirror = true }, _t["dest"], false, Folder(_t[@"src\Docs"]));

        Assert.All(results, r => Assert.InRange(r.ExitCode, 0, 7));
        Assert.True(System.IO.File.Exists(_t[@"dest\Docs\keep.txt"]));
        Assert.False(System.IO.File.Exists(_t[@"dest\Docs\extra.txt"]));
        Assert.False(Directory.Exists(_t[@"dest\Docs\old"]));
        Assert.True(System.IO.File.Exists(_t[@"dest\unrelated.txt"]));
        Assert.True(System.IO.File.Exists(_t[@"dest\OtherFolder\x.txt"]));
    }

    [Fact]
    public void Mirror_backup_twice_reports_already_up_to_date()
    {
        _t.File(@"src\Docs\a.txt");
        _t.File(@"src\Docs\sub\b.txt");
        var mirror = RoboCopyTo.Core.Presets.Preset.BuiltIns.Single(p => p.Name == RoboCopyTo.Core.Presets.Preset.MirrorBackup).Options;

        var first = Assert.Single(Copy(mirror, _t["dest"], false, Folder(_t[@"src\Docs"])));
        var second = Assert.Single(Copy(mirror, _t["dest"], false, Folder(_t[@"src\Docs"])));

        Assert.Equal(1, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal("Already up to date", RoboCopyTo.Core.Execution.ExitCodeInterpreter.Interpret(second.ExitCode, mirror: true).Text);
    }

    [Fact]
    public void Dry_run_leaves_destination_unchanged()
    {
        _t.File(@"src\Docs\new.txt");
        _t.File(@"dest\Docs\extra.txt");
        var before = Snapshot(_t["dest"]);

        var results = Copy(_fast with { Mirror = true }, _t["dest"], dryRun: true, Folder(_t[@"src\Docs"]));

        Assert.All(results, r => Assert.InRange(r.ExitCode, 0, 7));
        Assert.Equal(before, Snapshot(_t["dest"]));
        Assert.True(results[0].ExitCode is 3, $"exit {results[0].ExitCode}");
    }

    public static TheoryData<ExistingFileMode, string[]> ExistingModeCases => new()
    {
        // Files expected to hold the SOURCE content afterwards.
        { ExistingFileMode.CopyIfDifferent, ["new", "srcNewer", "srcOlder", "changed"] },
        { ExistingFileMode.OnlyNewer, ["new", "srcNewer", "changed"] },
        { ExistingFileMode.SkipExisting, ["new"] },
        { ExistingFileMode.AlwaysOverwrite, ["new", "srcNewer", "srcOlder", "changed", "identical"] },
    };

    [Theory]
    [MemberData(nameof(ExistingModeCases))]
    public void Existing_file_modes(ExistingFileMode mode, string[] expectSourceContent)
    {
        var old = DateTime.Now.AddDays(-10);
        var recent = DateTime.Now.AddDays(-1);
        var same = DateTime.Now.AddDays(-5);
        // new: only in source
        _t.File(@"src\D\new.txt", "S-new", recent);
        // srcNewer: source newer than destination
        _t.File(@"src\D\srcNewer.txt", "S-srcNewer", recent);
        _t.File(@"dest\D\srcNewer.txt", "D-srcNewer", old);
        // srcOlder: destination newer than source
        _t.File(@"src\D\srcOlder.txt", "S-srcOlder", old);
        _t.File(@"dest\D\srcOlder.txt", "D-srcOlder", recent);
        // changed: same timestamp, different size
        _t.File(@"src\D\changed.txt", "S-changed-longer", same);
        _t.File(@"dest\D\changed.txt", "D-chg", same);
        // identical: same timestamp, same size, different bytes (so overwriting is observable)
        _t.File(@"src\D\identical.txt", "S-identical", same);
        _t.File(@"dest\D\identical.txt", "D-identical", same);

        var results = Copy(_fast with { ExistingFiles = mode }, _t["dest"], false, Folder(_t[@"src\D"]));
        Assert.All(results, r => Assert.InRange(r.ExitCode, 0, 7));

        foreach (var name in new[] { "new", "srcNewer", "srcOlder", "changed", "identical" })
        {
            var content = System.IO.File.ReadAllText(_t[$@"dest\D\{name}.txt"]);
            var expected = expectSourceContent.Contains(name) ? "S-" : "D-";
            Assert.True(content.StartsWith(expected, StringComparison.Ordinal), $"{mode}: {name}.txt has '{content}', expected {expected}...");
        }
    }

    [Theory]
    [InlineData("My Documents")]
    [InlineData("Bob's files")]
    [InlineData("Résumé 日本語 файлы")]
    public void Paths_with_spaces_apostrophes_and_unicode(string name)
    {
        _t.File($@"src\{name}\{name}.txt", "x");
        _t.File($@"src\loose {name}.txt", "y");
        var dest = _t.Dir($"dest {name}");

        var results = Copy(_fast, dest, false, Folder(_t[$@"src\{name}"]), File(_t[$@"src\loose {name}.txt"]));

        Assert.All(results, r => Assert.InRange(r.ExitCode, 1, 7));
        Assert.True(System.IO.File.Exists(Path.Combine(dest, name, name + ".txt")));
        Assert.True(System.IO.File.Exists(Path.Combine(dest, $"loose {name}.txt")));
    }

    [Fact]
    public void Trailing_backslashes_on_sources_and_destination()
    {
        _t.File(@"src\Docs\a.txt");
        var dest = _t.Dir("dest") + "\\";

        var results = Copy(_fast, dest, false, Folder(_t[@"src\Docs"] + "\\"));

        Assert.All(results, r => Assert.InRange(r.ExitCode, 1, 7));
        Assert.True(System.IO.File.Exists(_t[@"dest\Docs\a.txt"]));
    }

    [Fact]
    public void Paths_longer_than_260_characters()
    {
        var deep = string.Join('\\', Enumerable.Repeat("a-long-folder-name-for-testing", 10));
        var srcFolder = _t[$@"src\{deep}"];
        Assert.True(srcFolder.Length > 260);
        Directory.CreateDirectory(srcFolder);
        System.IO.File.WriteAllText(Path.Combine(srcFolder, "deep file.txt"), "deep");
        var dest = _t.Dir("dest");

        var results = Copy(_fast, dest, false, Folder(_t[@"src\a-long-folder-name-for-testing"]), File(Path.Combine(srcFolder, "deep file.txt")));

        Assert.All(results, r => Assert.InRange(r.ExitCode, 1, 7));
        var copied = Path.Combine(dest, deep, "deep file.txt");
        Assert.True(copied.Length > 260);
        Assert.True(System.IO.File.Exists(copied));
        Assert.True(System.IO.File.Exists(Path.Combine(dest, "deep file.txt")));
    }

    [Fact]
    public void Locked_file_produces_failure_exit_code()
    {
        _t.File(@"src\D\ok.txt");
        var locked = _t.File(@"src\D\locked.txt", "locked");
        using (System.IO.File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var results = Copy(_fast, _t["dest"], false, Folder(_t[@"src\D"]));
            var r = Assert.Single(results);
            Assert.True(r.ExitCode >= 8, $"exit {r.ExitCode}");
            Assert.Contains("ERROR 32", r.Output);
        }
        Assert.True(System.IO.File.Exists(_t[@"dest\D\ok.txt"]));
    }

    private static string Snapshot(string root)
        => string.Join("\n", Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(p => p + "|" + (System.IO.File.Exists(p) ? new FileInfo(p).Length + "|" + System.IO.File.GetLastWriteTimeUtc(p).Ticks : "dir")));
}
