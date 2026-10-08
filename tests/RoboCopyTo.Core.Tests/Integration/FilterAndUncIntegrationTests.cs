using RoboCopyTo.Core.Options;
using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.Core.Tests.Integration;

/// <summary>Real robocopy: exclude patterns with spaces, and a real copy to a UNC destination.</summary>
[Collection("robocopy")]
public sealed class FilterAndUncIntegrationTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly RobocopyOptions _fast = new() { RetryCount = 0, RetryWaitSeconds = 0 };

    public void Dispose() => _t.Dispose();

    [Fact]
    public void Exclude_patterns_with_spaces_and_trailing_backslashes_are_honored()
    {
        _t.File(@"src\D\keep.txt");
        _t.File(@"src\D\skip me.txt");
        _t.File(@"src\D\My Folder\inner.txt");
        _t.File(@"src\D\other\kept.txt");
        var o = _fast with { ExcludeFiles = "\"skip me.txt\"", ExcludeFolders = "\"My Folder\\\"" };

        var jobs = CommandBuilder.Build([new SelectedItem(_t[@"src\D"], ItemKind.Folder)], _t["dest"], o, new BuildContext(_t["l.log"]));
        var r = Robo.Run(jobs[0].Arguments);

        Assert.InRange(r.ExitCode, 1, 7);
        Assert.True(File.Exists(_t[@"dest\D\keep.txt"]));
        Assert.True(File.Exists(_t[@"dest\D\other\kept.txt"]));
        Assert.False(File.Exists(_t[@"dest\D\skip me.txt"]));
        Assert.False(Directory.Exists(_t[@"dest\D\My Folder"]));
    }

    [Fact]
    public void Copy_to_a_unc_destination()
    {
        _t.File(@"src\Docs\a.txt", "unc");
        _t.File(@"src\loose.txt", "loose");
        var drive = Path.GetPathRoot(_t.Root)![0];
        var uncDest = $@"\\localhost\{drive}$\{_t.Dir("dest")[3..]}";

        var jobs = CommandBuilder.Build(
            [new SelectedItem(_t[@"src\Docs"], ItemKind.Folder), new SelectedItem(_t[@"src\loose.txt"], ItemKind.File)],
            uncDest, _fast, new BuildContext(_t["l.log"]));
        var results = Robo.RunAll(jobs);

        Assert.All(results, r => Assert.InRange(r.ExitCode, 1, 7));
        Assert.Equal("unc", File.ReadAllText(_t[@"dest\Docs\a.txt"]));
        Assert.Equal("loose", File.ReadAllText(_t[@"dest\loose.txt"]));
    }

    [Fact]
    public void Dash_mir_in_extra_arguments_really_mirrors_so_the_checks_must_catch_it()
    {
        // Documents why safety checks normalize switches: robocopy treats -MIR as /MIR.
        _t.File(@"src\D\a.txt");
        _t.File(@"dest\D\extra.txt");
        var jobs = CommandBuilder.Build([new SelectedItem(_t[@"src\D"], ItemKind.Folder)], _t["dest"],
            _fast with { ExtraArguments = "-MIR" }, new BuildContext(_t["l.log"], DryRun: true));
        var r = Robo.Run(jobs[0].Arguments);
        Assert.Contains("/PURGE", r.Output);
        Assert.True(jobs[0].DeletesExtras);
    }
}
