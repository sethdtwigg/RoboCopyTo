using RoboCopyTo.Core.Options;
using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.Core.Tests.Planning;

public class ArgumentTokenizerTests
{
    [Theory]
    [InlineData("/MIR", "/MIR")]
    [InlineData("-mir", "/MIR")]
    [InlineData("\"/Purge\"", "/PURGE")]
    [InlineData("\"-move\"", "/MOVE")]
    [InlineData("/lev:2", "/LEV:2")]
    public void Normalizes_switch_spellings(string token, string expected)
        => Assert.Equal(expected, ArgumentTokenizer.NormalizeSwitch(token));

    [Fact]
    public void Splits_on_whitespace_and_keeps_quoted_runs()
        => Assert.Equal(["/XF", "\"a b.txt\"", "c.txt"], ArgumentTokenizer.Split("  /XF  \"a b.txt\" c.txt "));

    [Fact]
    public void Contains_switch_matches_only_whole_tokens()
    {
        Assert.True(ArgumentTokenizer.ContainsSwitch("/NDL -Purge", "/PURGE"));
        Assert.False(ArgumentTokenizer.ContainsSwitch("/PURGED purge.txt", "/PURGE"));
    }

    [Theory]
    [InlineData("\"C:\\My Dir\\\"", "/XD \"C:\\My Dir\"")]
    [InlineData("node_modules\\", "/XD node_modules")]
    [InlineData("E:\\", "/XD E:\\")]
    public void Exclude_patterns_never_end_in_a_quote_escaping_backslash(string pattern, string expected)
    {
        var job = Assert.Single(CommandBuilder.Build([new SelectedItem(@"C:\A", ItemKind.Folder)], @"D:\B",
            new RobocopyOptions { ExcludeFolders = pattern }, new BuildContext(@"C:\l.log")));
        Assert.Contains(expected + " ", job.Arguments);
    }

    [Theory]
    [InlineData(@"\\?\C:\Data\x", @"C:\Data\x")]
    [InlineData(@"\\?\UNC\srv\share\x", @"\\srv\share\x")]
    [InlineData(@"C:\Data", @"C:\Data")]
    public void Normalize_strips_long_path_prefixes(string input, string expected)
        => Assert.Equal(expected, PathUtil.Normalize(input));

    [Fact]
    public void Null_option_strings_do_not_break_the_builder()
    {
        var o = new RobocopyOptions { ExtraArguments = null!, ExcludeFiles = null!, ExcludeFolders = null! };
        var job = Assert.Single(CommandBuilder.Build([new SelectedItem(@"C:\A", ItemKind.Folder)], @"D:\B", o, new BuildContext(@"C:\l.log")));
        Assert.EndsWith("/UNILOG+:\"C:\\l.log\"", job.Arguments);
    }

    [Fact]
    public void Cancel_warning_applies_only_without_restartable_mode()
    {
        Assert.NotEqual("", OptionHelp.CancelWarning(new RobocopyOptions(), dryRun: false));
        Assert.Equal("", OptionHelp.CancelWarning(new RobocopyOptions { Restartable = true }, dryRun: false));
        Assert.Equal("", OptionHelp.CancelWarning(new RobocopyOptions { RestartableBackup = true }, dryRun: false));
        Assert.Equal("", OptionHelp.CancelWarning(new RobocopyOptions(), dryRun: true));
    }
}
