using System.Text;
using RoboCopyTo.Core.Execution;
using RoboCopyTo.Core.Logging;
using RoboCopyTo.Core.Options;
using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.Core.Tests.Integration;

[Collection("robocopy")]
public sealed class RunSessionTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly RobocopyOptions _fast = new() { RetryCount = 0, RetryWaitSeconds = 0 };

    public void Dispose()
    {
        RobocopyRunner.RobocopyPathOverride = null;
        _t.Dispose();
    }

    private (RunSession Session, IReadOnlyList<RobocopyJob> Jobs) Make(RobocopyOptions o, params string[] folders)
    {
        var logs = new LogManager(_t["Logs"]);
        var log = logs.ReserveLogPath(DateTime.Now, false);
        logs.WriteHeader(log, new LogHeader(DateTime.Now, "t", folders, _t["dest"], [], false));
        var jobs = CommandBuilder.Build(folders.Select(f => new SelectedItem(_t[f], ItemKind.Folder)).ToList(), _t["dest"], o, new BuildContext(log));
        return (new RunSession(jobs, PreScanner.Scan(jobs, o)), jobs);
    }

    [Fact]
    public async Task Cancel_before_start_marks_every_job_cancelled_and_runs_nothing()
    {
        _t.File(@"A\a.txt");
        _t.File(@"B\b.txt");
        var (session, _) = Make(_fast, "A", "B");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await session.RunAsync(cts.Token);

        Assert.True(result.Cancelled);
        Assert.False(result.AllSucceeded);
        Assert.All(session.Statuses, s => Assert.Equal(JobStatus.Cancelled, s));
        Assert.False(Directory.Exists(_t["dest"]));
    }

    [Fact]
    public async Task Progress_is_cumulative_across_jobs_and_ends_at_the_grand_total()
    {
        _t.File(@"A\a.txt", new string('a', 1000));
        _t.File(@"B\b.txt", new string('b', 3000));
        var (session, _) = Make(_fast, "A", "B");
        var reports = new List<OverallProgress>();
        session.ProgressChanged += p => { lock (reports) reports.Add(p); };

        var result = await session.RunAsync(CancellationToken.None);

        Assert.True(result.AllSucceeded);
        Assert.Equal(new ScanTotals(2, 4000), session.GrandTotal);
        Assert.Contains(reports, p => p.BytesDone == 1000 && p.BytesTotal == 4000);
        Assert.Equal(1.0, reports[^1].Fraction);
        Assert.Equal(2, result.Totals.Copied);
    }

    [Fact]
    public async Task A_job_whose_robocopy_cannot_start_is_failed_not_stuck()
    {
        _t.File(@"A\a.txt");
        var (session, _) = Make(_fast, "A");
        RobocopyRunner.RobocopyPathOverride = _t["no-such-robocopy.exe"];
        var lines = new List<string>();
        session.OutputLine += lines.Add;

        var result = await session.RunAsync(CancellationToken.None);

        Assert.Equal(JobStatus.Failed, session.Statuses[0]);
        Assert.False(result.AllSucceeded);
        Assert.False(result.Cancelled);
        Assert.StartsWith("Robocopy could not be started", result.Results[0]!.Interpretation.Text);
        Assert.Contains(lines, l => l.StartsWith("Robocopy could not be started", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancelling_after_a_job_finished_keeps_its_real_result()
    {
        _t.File(@"A\a.txt");
        var (session, jobs) = Make(_fast, "A");
        using var cts = new CancellationTokenSource();
        var r = await RobocopyRunner.RunAsync(jobs[0], ct: cts.Token);
        cts.Cancel(); // after exit: must not rewrite the outcome

        Assert.False(r.Cancelled);
        Assert.Equal(1, r.ExitCode);
        _ = session;
    }
}

public sealed class LogReservationTests : IDisposable
{
    private readonly TempDir _t = new();

    public void Dispose() => _t.Dispose();

    [Fact]
    public void Concurrent_reservations_in_the_same_second_get_distinct_files()
    {
        var logs = new LogManager(_t["Logs"]);
        var when = new DateTime(2026, 10, 8, 12, 0, 0);
        var paths = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 20, _ => paths.Add(logs.ReserveLogPath(when, false)));

        Assert.Equal(20, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(paths, p => p.EndsWith("2026-10-08_120000.log", StringComparison.Ordinal));
        Assert.Equal(20, Directory.GetFiles(logs.Directory).Length);
    }

    [Fact]
    public void Header_written_to_a_reserved_file_is_utf16()
    {
        var logs = new LogManager(_t["Logs"]);
        var path = logs.ReserveLogPath(DateTime.Now, true);
        logs.WriteHeader(path, new LogHeader(DateTime.Now, "1", ["C:\\x"], "D:\\y", [], true));
        Assert.EndsWith("_dryrun.log", path);
        Assert.StartsWith("RoboCopyTo run log", File.ReadAllText(path, Encoding.Unicode));
    }
}
