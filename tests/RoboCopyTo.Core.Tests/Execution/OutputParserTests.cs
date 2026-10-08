using System.Text;
using RoboCopyTo.Core.Execution;

namespace RoboCopyTo.Core.Tests.Execution;

/// <summary>
/// Fixtures are real robocopy output captured with /BYTES /FP /NP /TEE, with and without /MT
/// (paths rewritten to C:\Fixture). The *.oem.txt files are raw stdout bytes in code page 437.
/// </summary>
public class OutputParserTests
{
    static OutputParserTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static string Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        return name.EndsWith(".utf16.log", StringComparison.Ordinal)
            ? File.ReadAllText(path, Encoding.Unicode)
            : Encoding.GetEncoding(437).GetString(File.ReadAllBytes(path));
    }

    private static OutputParser Parse(string name)
    {
        var parser = new OutputParser();
        parser.FeedText(Fixture(name));
        parser.Complete();
        return parser;
    }

    [Theory]
    [InlineData("copy_st.oem.txt")]
    [InlineData("copy_mt.oem.txt")]
    [InlineData("mir_st.oem.txt")]
    [InlineData("mir_mt.oem.txt")]
    [InlineData("locked_r0_st.oem.txt")]
    [InlineData("locked_r0_mt.oem.txt")]
    public void Copy_with_locked_file_counts_copied_files_and_one_failure(string fixture)
    {
        var p = Parse(fixture);

        Assert.Equal(7, p.FilesDone);
        Assert.Equal(5_000_039, p.BytesDone);

        var failure = Assert.Single(p.Failures);
        Assert.Equal(@"C:\Fixture\src\locked.txt", failure.Path);
        Assert.Equal(32, failure.ErrorCode);
        Assert.Equal("Copying File", failure.Action);
        Assert.Equal("The process cannot access the file because it is being used by another process.", failure.Message);
    }

    [Theory]
    [InlineData("copy_st.oem.txt")]
    [InlineData("copy_mt.oem.txt")]
    public void Summary_table_is_parsed(string fixture)
    {
        var s = Parse(fixture).Summary;
        Assert.NotNull(s);
        Assert.Equal(new SummaryRow(9, 7, 1, 0, 1, 1), s.Files);
        Assert.Equal(new SummaryRow(5_000_057, 5_000_039, 4, 0, 14, 5), s.Bytes);
        Assert.Equal(2, s.Dirs.Total);
    }

    [Theory]
    [InlineData("many_st.oem.txt")]
    [InlineData("many_mt.oem.txt")]
    [InlineData("many_st.utf16.log")]
    [InlineData("many_mt.utf16.log")]
    public void Many_files_reach_full_count_with_and_without_MT(string fixture)
    {
        var p = Parse(fixture);
        Assert.Equal(202, p.FilesDone);
        Assert.Equal(50_412_600, p.BytesDone);
        Assert.Empty(p.Failures);
        Assert.Equal(new SummaryRow(202, 202, 0, 0, 0, 0), p.Summary!.Files);
    }

    [Theory]
    [InlineData("uptodate_st.oem.txt")]
    [InlineData("uptodate_mt.oem.txt")]
    public void Up_to_date_run_counts_nothing_and_reports_skips(string fixture)
    {
        var p = Parse(fixture);
        Assert.Equal(0, p.FilesDone);
        Assert.Equal(0, p.BytesDone);
        Assert.Empty(p.Failures);
        Assert.Equal(new SummaryRow(9, 0, 9, 0, 0, 1), p.Summary!.Files);
        Assert.Equal(1, p.ExtrasListed);
    }

    [Fact]
    public void Dry_run_lists_files_that_would_be_copied()
    {
        var p = Parse("dry_st.oem.txt");
        Assert.Equal(8, p.FilesDone);
        Assert.Equal(5_000_053, p.BytesDone);
        Assert.Empty(p.Failures);
    }

    [Fact]
    public void Loose_file_job_output()
    {
        var p = Parse("files_st.oem.txt");
        Assert.Equal(2, p.FilesDone);
        Assert.Equal(5_000_009, p.BytesDone);
        Assert.Equal(new SummaryRow(3, 2, 1, 0, 0, 0), p.Summary!.Files);
    }

    [Fact]
    public void Extra_files_are_not_counted_as_progress()
    {
        var p = Parse("copy_st.oem.txt");
        Assert.Equal(1, p.ExtrasListed);
        Assert.DoesNotContain(p.Failures, f => f.Path.Contains("extra_in_dst"));
    }

    [Fact]
    public void Oem_decoding_keeps_latin_accents()
    {
        Assert.Contains(@"C:\Fixture\src\café résumé.txt", Fixture("copy_st.oem.txt"));
    }

    [Fact]
    public void Utf16_log_keeps_header_and_non_latin_names()
    {
        var log = Fixture("many_st.utf16.log");
        Assert.StartsWith("RoboCopyTo header line", log);
        Assert.DoesNotContain('\uFEFF', log);
    }

    [Fact]
    public void MT_run_on_line_is_split()
    {
        // Real /MT output: a retry notice is glued to the end of an unrelated file line.
        var p = new OutputParser();
        p.Feed("\t    New File  \t\t       4\tC:\\Fixture\\src\\sub\\deep.txtWaiting 1 seconds...");
        p.Complete();
        Assert.Equal(1, p.FilesDone);
        Assert.Equal(4, p.BytesDone);
        Assert.Equal(@"C:\Fixture\src\sub\deep.txt", p.CurrentFile);
    }

    [Fact]
    public void Retry_that_succeeds_clears_the_failure()
    {
        var p = new OutputParser();
        p.FeedText("""
            	    New File  		      14	C:\x\locked.txt
            2026/10/08 15:12:22 ERROR 32 (0x00000020) Copying File C:\x\locked.txt
            The process cannot access the file because it is being used by another process.
            Waiting 1 seconds... Retrying...
            	    New File  		      14	C:\x\locked.txt
            	    New File  		       9	C:\x\new.txt

            """);
        p.Complete();
        Assert.Empty(p.Failures);
        Assert.Equal(2, p.FilesDone);
        Assert.Equal(23, p.BytesDone);
    }

    [Fact]
    public void Non_file_errors_are_reported()
    {
        var p = new OutputParser();
        p.FeedText("""
            2026/10/08 15:11:09 ERROR 3 (0x00000003) Accessing Source Directory C:\missing\
            The system cannot find the path specified.

            """);
        p.Complete();
        var f = Assert.Single(p.Failures);
        Assert.Equal(@"C:\missing\", f.Path);
        Assert.Equal("Accessing Source Directory", f.Action);
        Assert.Equal(3, f.ErrorCode);
        Assert.Equal("The system cannot find the path specified.", f.Message);
    }

    [Fact]
    public void Text_fed_in_arbitrary_chunks_parses_the_same()
    {
        var text = Fixture("copy_mt.oem.txt");
        var p = new OutputParser();
        for (var i = 0; i < text.Length; i += 37)
            p.FeedText(text.Substring(i, Math.Min(37, text.Length - i)));
        p.Complete();
        Assert.Equal(7, p.FilesDone);
        Assert.Single(p.Failures);
        Assert.NotNull(p.Summary);
    }

    [Fact]
    public void Lines_event_reports_each_complete_line()
    {
        var p = new OutputParser();
        var lines = new List<string>();
        p.LineParsed += lines.Add;
        p.FeedText("a\r\nb\nc");
        Assert.Equal(["a", "b"], lines);
        p.Complete();
        Assert.Equal(["a", "b", "c"], lines);
    }
}
