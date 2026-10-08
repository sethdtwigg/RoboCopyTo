using System.Diagnostics;
using System.Text;
using RoboCopyTo.Core.Execution;
using RoboCopyTo.Core.Logging;
using RoboCopyTo.Core.Options;
using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.Core.Tests.Integration;

/// <summary>Full pipeline: build, pre-scan, log header, run, parse. Temp folders only.</summary>
[Collection("robocopy")]
public sealed class RunnerIntegrationTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly RobocopyOptions _fast = new() { RetryCount = 0, RetryWaitSeconds = 0 };

    public void Dispose() => _t.Dispose();

    private sealed record Run(RunResult Result, List<OverallProgress> Progress, List<string> Output, string LogPath, RunSession Session);

    private async Task<Run> Execute(RobocopyOptions options, bool dryRun, string dest, SelectedItem[] items, CancellationToken ct = default, Action<RunSession>? onStart = null)
    {
        var logs = new LogManager(_t["Logs"]);
        var logPath = logs.NewLogPath(DateTime.Now, dryRun);
        var jobs = CommandBuilder.Build(items, dest, options, new BuildContext(logPath, dryRun));
        var totals = PreScanner.Scan(jobs, options);
        logs.WriteHeader(logPath, new LogHeader(DateTime.Now, "test", items.Select(i => i.Path).ToList(), dest, jobs.Select(j => j.CommandLine).ToList(), dryRun));

        var session = new RunSession(jobs, totals);
        var progress = new List<OverallProgress>();
        var output = new List<string>();
        session.ProgressChanged += p => { lock (progress) progress.Add(p); };
        session.OutputLine += l => { lock (output) output.Add(l); };
        onStart?.Invoke(session);
        var result = await session.RunAsync(ct);
        return new Run(result, progress, output, logPath, session);
    }

    private static SelectedItem Folder(string p) => new(p, ItemKind.Folder);
    private static SelectedItem File(string p) => new(p, ItemKind.File);

    private void MakeTree()
    {
        for (var i = 0; i < 40; i++)
            _t.File($@"src\Data\sub{i % 4}\file{i}.bin", new string('x', 1000 + i));
        _t.File(@"src\Data\big.bin", new string('y', 3_000_000));
        // Identical file already at the destination: robocopy skips it without listing it.
        var when = DateTime.Now.AddDays(-3);
        _t.File(@"src\Data\same.txt", "same", when);
        _t.File(@"dest\Data\same.txt", "same", when);
        _t.File(@"src\loose1.txt", "loose one");
        _t.File(@"src\loose2.txt", "loose two");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Progress_reaches_100_percent_with_and_without_MT(bool mt)
    {
        MakeTree();
        var run = await Execute(_fast with { Multithreaded = mt }, false, _t["dest"],
            [Folder(_t[@"src\Data"]), File(_t[@"src\loose1.txt"]), File(_t[@"src\loose2.txt"])]);

        Assert.True(run.Result.AllSucceeded);
        Assert.All(run.Session.Statuses, s => Assert.Equal(JobStatus.Done, s));
        var last = run.Progress[^1];
        Assert.Equal(1.0, last.Fraction);
        Assert.Equal(44, last.FilesTotal);
        Assert.Equal(last.FilesTotal, last.FilesDone);
        Assert.Equal(last.BytesTotal, last.BytesDone);
        Assert.Equal(new RunTotals(43, 1, 0, 0, 0), run.Result.Totals);
        Assert.True(run.Progress.Zip(run.Progress.Skip(1)).All(p => p.Second.BytesDone >= p.First.BytesDone), "progress never goes backwards");
    }

    [Fact]
    public async Task Cancel_stops_robocopy_within_two_seconds()
    {
        _t.File(@"src\Slow\huge.bin", new string('z', 40_000_000));
        using var cts = new CancellationTokenSource();
        var sw = new Stopwatch();
        var task = Execute(_fast with { Multithreaded = false, ExtraArguments = "/IPG:50" }, false, _t["dest"], [Folder(_t[@"src\Slow"])], cts.Token,
            onStart: s => s.JobStatusChanged += (index, status) =>
            {
                if (status == JobStatus.Running)
                    Task.Delay(1000).ContinueWith(t => { sw.Start(); cts.Cancel(); });
            });

        var run = await task;
        sw.Stop();

        Assert.True(run.Result.Cancelled);
        Assert.Equal(JobStatus.Cancelled, run.Session.Statuses[0]);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.Elapsed}");
        Assert.Equal("Cancelled", run.Result.Results[0]!.Interpretation.Text);
        // The copy was cut short: the destination file is absent or incomplete.
        var copied = new FileInfo(_t[@"dest\Slow\huge.bin"]);
        Assert.True(!copied.Exists || copied.Length < 40_000_000 || copied.LastWriteTime != System.IO.File.GetLastWriteTime(_t[@"src\Slow\huge.bin"]));
    }

    [Fact]
    public async Task Locked_file_appears_in_failures_and_marks_job_failed()
    {
        _t.File(@"src\D\ok.txt");
        var locked = _t.File(@"src\D\locked file.txt", "locked");
        Run run;
        using (System.IO.File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            run = await Execute(_fast, false, _t["dest"], [Folder(_t[@"src\D"])]);

        Assert.False(run.Result.AllSucceeded);
        Assert.Equal(JobStatus.Failed, run.Session.Statuses[0]);
        var jr = run.Result.Results[0]!;
        Assert.True(jr.ExitCode >= 8);
        Assert.Contains("Some items failed after retries", jr.Interpretation.Text);
        var f = Assert.Single(run.Result.Failures);
        Assert.Equal(locked, f.Path);
        Assert.Equal(32, f.ErrorCode);
        Assert.Equal(1, run.Result.Totals.Failed);
        Assert.Equal(1.0, run.Progress[^1].Fraction);
    }

    [Fact]
    public async Task Dry_run_changes_nothing_and_is_labeled()
    {
        MakeTree();
        _t.File(@"dest\Data\extra.txt", "extra");
        var before = Directory.GetFileSystemEntries(_t["dest"], "*", SearchOption.AllDirectories).Order().ToArray();

        var run = await Execute(_fast with { Mirror = true }, true, _t["dest"], [Folder(_t[@"src\Data"])]);

        Assert.True(run.Result.DryRun);
        Assert.EndsWith("_dryrun.log", run.LogPath);
        Assert.Equal(before, Directory.GetFileSystemEntries(_t["dest"], "*", SearchOption.AllDirectories).Order().ToArray());
        Assert.Equal("Files would be copied; Extra items at the destination would be removed", run.Result.Results[0]!.Interpretation.Text);
        Assert.Equal(1.0, run.Progress[^1].Fraction);
    }

    [Fact]
    public async Task Non_english_names_appear_correctly_in_output_lines()
    {
        _t.File(@"src\Intl\日本語ファイル.txt", "jp");
        _t.File(@"src\Intl\Ελληνικά.txt", "gr");
        _t.File(@"src\Intl\café.txt", "fr");

        var run = await Execute(_fast, false, _t["dest"], [Folder(_t[@"src\Intl"])]);

        Assert.True(run.Result.AllSucceeded);
        Assert.Contains(run.Output, l => l.EndsWith(@"\日本語ファイル.txt", StringComparison.Ordinal));
        Assert.Contains(run.Output, l => l.EndsWith(@"\Ελληνικά.txt", StringComparison.Ordinal));
        Assert.Contains(run.Output, l => l.EndsWith(@"\café.txt", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Output, l => l.Contains("???"));
    }

    [Fact]
    public async Task Log_is_one_readable_utf16_file_with_header_and_robocopy_output()
    {
        MakeTree();
        var run = await Execute(_fast, false, _t["dest"], [Folder(_t[@"src\Data"]), File(_t[@"src\loose1.txt"])]);

        var bytes = System.IO.File.ReadAllBytes(run.LogPath);
        Assert.Equal([0xFF, 0xFE], bytes[..2]);
        var text = System.IO.File.ReadAllText(run.LogPath, Encoding.Unicode);
        Assert.StartsWith("RoboCopyTo run log", text);
        Assert.Contains("[1] robocopy \"", text);
        Assert.Contains("[2] robocopy \"", text);
        Assert.Contains("ROBOCOPY     ::     Robust File Copy for Windows", text);
        Assert.Contains(@"\Data\big.bin", text);
        Assert.Equal(2, CountOccurrences(text, "Total    Copied   Skipped"));
        Assert.DoesNotContain('﻿', text[1..]);
        Assert.Single(Directory.GetFiles(_t["Logs"]));
    }

    [Fact]
    public async Task Retry_failed_reruns_the_same_calls_and_copies_what_failed()
    {
        _t.File(@"src\D\ok.txt");
        var locked = _t.File(@"src\D\locked.txt", "locked");
        var items = new[] { Folder(_t[@"src\D"]) };
        Run first;
        using (System.IO.File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            first = await Execute(_fast, false, _t["dest"], items);
        Assert.Single(first.Result.Failures);

        var second = await Execute(_fast, false, _t["dest"], items);

        Assert.True(second.Result.AllSucceeded);
        Assert.Equal(new RunTotals(1, 1, 0, 0, 0), second.Result.Totals);
        Assert.True(System.IO.File.Exists(_t[@"dest\D\locked.txt"]));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
            count++;
        return count;
    }
}
