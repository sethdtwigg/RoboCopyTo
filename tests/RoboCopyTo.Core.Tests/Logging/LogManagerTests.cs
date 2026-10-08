using System.Text;
using RoboCopyTo.Core.Logging;
using RoboCopyTo.Core.Tests.Integration;

namespace RoboCopyTo.Core.Tests.Logging;

public class LogManagerTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 15, 30, 45, DateTimeKind.Utc);

    [Theory]
    [InlineData(false, "2026-10-08_153045.log")]
    [InlineData(true, "2026-10-08_153045_dryrun.log")]
    public void File_name_format(bool dryRun, string expected) => Assert.Equal(expected, LogManager.FileNameFor(Now, dryRun));

    [Theory]
    [InlineData(31, true)]
    [InlineData(30.01, true)]
    [InlineData(29.99, false)]
    [InlineData(1, false)]
    [InlineData(0, false)]
    public void Retention_is_thirty_days(double ageDays, bool expired)
        => Assert.Equal(expired, LogManager.IsExpired(Now.AddDays(-ageDays), Now));

    [Fact]
    public void Header_contains_date_version_sources_destination_and_commands()
    {
        var text = LogManager.FormatHeader(new LogHeader(Now, "1.2.3", [@"C:\A", @"C:\Docs\a.txt"], @"D:\Backup",
            ["robocopy \"C:\\A\" \"D:\\Backup\\A\" /E", "robocopy \"C:\\Docs\" \"D:\\Backup\" \"a.txt\""], DryRun: true));
        Assert.Contains("2026-10-08 15:30:45", text);
        Assert.Contains("1.2.3", text);
        Assert.Contains(@"  C:\A", text);
        Assert.Contains(@"  C:\Docs\a.txt", text);
        Assert.Contains(@"Destination : D:\Backup", text);
        Assert.Contains("[1] robocopy \"C:\\A\" \"D:\\Backup\\A\" /E", text);
        Assert.Contains("[2] robocopy \"C:\\Docs\" \"D:\\Backup\" \"a.txt\"", text);
        Assert.Contains("DRY RUN", text);
    }

    [Fact]
    public void Header_is_written_as_utf16le_with_bom()
    {
        using var t = new TempDir();
        var lm = new LogManager(t["Logs"]);
        var path = lm.NewLogPath(Now, false);
        lm.WriteHeader(path, new LogHeader(Now, "1.0", [@"C:\日本語"], @"D:\x", ["robocopy"], false));
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xFE, bytes[1]);
        Assert.Contains(@"C:\日本語", File.ReadAllText(path, Encoding.Unicode));
    }

    [Fact]
    public void New_log_path_avoids_existing_file()
    {
        using var t = new TempDir();
        var lm = new LogManager(t.Dir("Logs"));
        File.WriteAllText(Path.Combine(lm.Directory, "2026-10-08_153045.log"), "");
        Assert.EndsWith("2026-10-08_153045-2.log", lm.NewLogPath(Now, false));
    }

    [Fact]
    public void Deletes_only_expired_log_files_in_the_log_folder()
    {
        using var t = new TempDir();
        var lm = new LogManager(t.Dir("Logs"));
        string Make(string rel, double ageDays)
        {
            var p = Path.Combine(lm.Directory, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, "x");
            File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddDays(-ageDays));
            return p;
        }
        var oldLog = Make("old.log", 45);
        var oldDry = Make("old_dryrun.log", 31);
        var newLog = Make("new.log", 29);
        var oldText = Make("old.txt", 60);
        var oldLogx = Make("old.logx", 60);
        var nested = Make(@"sub\old.log", 60);

        var deleted = lm.DeleteExpiredLogs(DateTime.UtcNow);

        Assert.Equal(2, deleted);
        Assert.False(File.Exists(oldLog));
        Assert.False(File.Exists(oldDry));
        Assert.True(File.Exists(newLog));
        Assert.True(File.Exists(oldText));
        Assert.True(File.Exists(oldLogx));
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Missing_log_folder_is_fine()
    {
        using var t = new TempDir();
        Assert.Equal(0, new LogManager(t["nope"]).DeleteExpiredLogs(DateTime.UtcNow));
    }

    [Fact]
    public void Tailer_reads_appended_text_across_odd_chunk_boundaries()
    {
        using var t = new TempDir();
        var path = t["tail.log"];
        File.WriteAllText(path, "HEADER\r\n", LogManager.LogEncoding);
        using var tailer = new LogTailer(path, LogTailer.CurrentLength(path));
        Assert.Equal("", tailer.ReadNew());

        var payload = new UnicodeEncoding(false, false).GetBytes("ファイル é\r\n");
        using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            fs.Write(payload, 0, 3); // split in the middle of a UTF-16 code unit
        var first = tailer.ReadNew();
        using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            fs.Write(payload, 3, payload.Length - 3);
        Assert.Equal("ファイル é\r\n", first + tailer.ReadNew());
    }
}
