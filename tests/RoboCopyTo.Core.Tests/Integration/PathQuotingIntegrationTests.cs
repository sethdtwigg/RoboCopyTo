using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.Core.Tests.Integration;

/// <summary>
/// Establishes how robocopy parses root and UNC paths. These use /L (list only), so nothing is written
/// outside the temp folder even when a drive root is named as the destination.
/// </summary>
[Collection("robocopy")]
public class PathQuotingIntegrationTests : IDisposable
{
    private readonly TempDir _t = new();

    public void Dispose() => _t.Dispose();

    private string Src()
    {
        _t.File(@"src\a.txt");
        return _t["src"];
    }

    [Fact]
    public void Naive_root_quoting_breaks_robocopy_parsing()
    {
        // Documents why PathUtil doubles the backslash: "C:\" escapes the closing quote.
        var r = Robo.Run($"\"{Src()}\" \"C:\\\" /L /LEV:1 /NJS");
        Assert.Equal(16, r.ExitCode);
    }

    [Fact]
    public void Drive_root_destination_quoted_by_PathUtil_is_understood()
    {
        var r = Robo.Run($"{PathUtil.Quote(Src())} {PathUtil.Quote(@"C:\")} /L /LEV:1 /NJS");
        Assert.InRange(r.ExitCode, 0, 7);
        Assert.Equal(@"C:\", Robo.HeaderValue(r.Output, "Dest :"));
    }

    [Fact]
    public void Trailing_backslash_on_a_quoted_folder_breaks_parsing()
    {
        var r = Robo.Run($"\"{Src()}\\\" \"{_t["dest"]}\" /L /NJS");
        Assert.Equal(16, r.ExitCode);
    }

    [Fact]
    public void Folder_with_trailing_backslash_quoted_by_PathUtil_is_understood()
    {
        var r = Robo.Run($"{PathUtil.Quote(Src() + "\\")} {PathUtil.Quote(_t["dest"] + "\\")} /L /NJS");
        Assert.InRange(r.ExitCode, 0, 7);
        Assert.Equal(_t["src"] + "\\", Robo.HeaderValue(r.Output, "Source :"));
        Assert.Equal(_t["dest"] + "\\", Robo.HeaderValue(r.Output, "Dest :"));
    }

    [Fact]
    public void Unc_share_root_and_folder_quoted_by_PathUtil_are_understood()
    {
        // The administrative share for the system drive points at the same temp folder.
        var drive = Path.GetPathRoot(_t.Root)![0];
        var uncSrc = $@"\\localhost\{drive}$\{Src()[3..]}";
        var uncShareRoot = $@"\\localhost\{drive}$\";

        var r = Robo.Run($"{PathUtil.Quote(uncSrc + "\\")} {PathUtil.Quote(uncShareRoot)} /L /LEV:1 /NJS");

        Assert.InRange(r.ExitCode, 0, 7);
        Assert.Equal(uncSrc + "\\", Robo.HeaderValue(r.Output, "Source :"));
        Assert.Equal($@"\\localhost\{drive}$\", Robo.HeaderValue(r.Output, "Dest :"));
    }

    [Fact]
    public void Unc_share_root_with_trailing_backslash_breaks_parsing()
    {
        var drive = Path.GetPathRoot(_t.Root)![0];
        var r = Robo.Run($"\"\\\\localhost\\{drive}$\\\" \"{_t["dest"]}\" /L /LEV:1 /NJS");
        Assert.Equal(16, r.ExitCode);
    }
}
