using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.Core.Tests.Planning;

public class PathUtilTests
{
    [Theory]
    [InlineData(@"C:\Data\Photos", "\"C:\\Data\\Photos\"")]
    [InlineData(@"C:\Data\Photos\", "\"C:\\Data\\Photos\"")]
    [InlineData(@"C:\Data\Photos\\\", "\"C:\\Data\\Photos\"")]
    [InlineData(@"E:\", "\"E:\\\\\"")]
    [InlineData(@"E:", "\"E:\\\\\"")]
    [InlineData(@"e:\", "\"E:\\\\\"")]
    [InlineData(@"\\server\share", "\"\\\\server\\share\"")]
    [InlineData(@"\\server\share\", "\"\\\\server\\share\"")]
    [InlineData(@"\\server\share\dir\", "\"\\\\server\\share\\dir\"")]
    [InlineData(@"C:\My Files\Bob's ""stuff""", null)] // quotes are invalid in Windows paths
    public void Quote_produces_robocopy_safe_form(string input, string? expected)
    {
        if (expected is null)
        {
            Assert.Throws<ArgumentException>(() => PathUtil.Quote(input));
            return;
        }
        Assert.Equal(expected, PathUtil.Quote(input));
    }

    [Theory]
    [InlineData(@"C:\My Files\Bob's résumé 日本語")]
    [InlineData(@"C:\a b\c'd")]
    public void Quote_keeps_spaces_apostrophes_and_unicode_verbatim(string path)
    {
        Assert.Equal("\"" + path + "\"", PathUtil.Quote(path));
    }

    [Fact]
    public void Quote_handles_paths_longer_than_260_characters()
    {
        var path = @"C:\" + string.Join('\\', Enumerable.Repeat("a-rather-long-folder-name", 15));
        Assert.True(path.Length > 260);
        Assert.Equal("\"" + path + "\"", PathUtil.Quote(path + "\\"));
    }

    [Theory]
    [InlineData(@"C:\", true)]
    [InlineData(@"C:", true)]
    [InlineData(@"C:\Data", false)]
    [InlineData(@"\\server\share", false)]
    public void IsDriveRoot(string path, bool expected) => Assert.Equal(expected, PathUtil.IsDriveRoot(path));

    [Theory]
    [InlineData(@"\\server\share", true)]
    [InlineData(@"\\server\share\", true)]
    [InlineData(@"\\server\share\dir", false)]
    [InlineData(@"C:\", false)]
    public void IsUncShareRoot(string path, bool expected) => Assert.Equal(expected, PathUtil.IsUncShareRoot(path));

    [Theory]
    [InlineData(@"C:\a\b", @"C:\a", true)]
    [InlineData(@"C:\A\B\c", @"c:\a\", true)]
    [InlineData(@"C:\a", @"C:\a", false)]
    [InlineData(@"C:\ab", @"C:\a", false)]
    [InlineData(@"C:\a", @"C:\a\b", false)]
    [InlineData(@"E:\x", @"E:\", true)]
    [InlineData(@"\\s\share\x", @"\\s\share", true)]
    public void IsStrictlyInside(string child, string parent, bool expected)
        => Assert.Equal(expected, PathUtil.IsStrictlyInside(child, parent));

    [Theory]
    [InlineData(@"C:\a\b\", @"c:\A\B", true)]
    [InlineData(@"C:\a\b", @"C:\a\bc", false)]
    public void AreSame(string a, string b, bool expected) => Assert.Equal(expected, PathUtil.AreSame(a, b));

    [Theory]
    [InlineData(@"C:\Data\Photos\", "Photos")]
    [InlineData(@"\\server\share", "share")]
    [InlineData(@"\\server\share\Docs", "Docs")]
    public void GetFolderName(string path, string expected) => Assert.Equal(expected, PathUtil.GetFolderName(path));
}
