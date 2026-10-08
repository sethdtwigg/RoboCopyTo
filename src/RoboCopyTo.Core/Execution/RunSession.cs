using System.Diagnostics;
using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.Core.Execution;

public enum JobStatus { Pending, Running, Done, Failed, Cancelled }

public sealed record OverallProgress(long FilesDone, long FilesTotal, long BytesDone, long BytesTotal, TimeSpan Elapsed, string? CurrentFile)
{
    /// <summary>0..1, by bytes when there are any, else by file count. An empty run is complete.</summary>
    public double Fraction => BytesTotal > 0 ? Math.Min(1.0, (double)BytesDone / BytesTotal)
        : FilesTotal > 0 ? Math.Min(1.0, (double)FilesDone / FilesTotal)
        : 1.0;
}

public sealed record RunTotals(long Copied, long Skipped, long Extras, long Mismatched, long Failed)
{
    public static RunTotals From(RobocopySummary? s)
        => s is null ? new(0, 0, 0, 0, 0) : new(s.Files.Copied, s.Files.Skipped, s.Files.Extras, s.Files.Mismatch, s.Files.Failed);

    public static RunTotals operator +(RunTotals a, RunTotals b)
        => new(a.Copied + b.Copied, a.Skipped + b.Skipped, a.Extras + b.Extras, a.Mismatched + b.Mismatched, a.Failed + b.Failed);
}

public sealed record RunResult(
    IReadOnlyList<RobocopyJob> Jobs,
    IReadOnlyList<JobRunResult?> Results,
    bool Cancelled,
    bool DryRun,
    TimeSpan Elapsed)
{
    public RunTotals Totals => Results.Aggregate(new RunTotals(0, 0, 0, 0, 0), (acc, r) => acc + RunTotals.From(r?.Summary));

    public IReadOnlyList<FailedItem> Failures => Results.Where(r => r is not null).SelectMany(r => r!.Failures).ToList();

    public bool AllSucceeded => !Cancelled && Results.All(r => r is { Interpretation.IsSuccess: true });
}

/// <summary>Runs a list of jobs one after another, tracking per-job status and overall progress.</summary>
public sealed class RunSession
{
    private readonly IReadOnlyList<RobocopyJob> _jobs;
    private readonly IReadOnlyList<ScanTotals> _totals;

    public RunSession(IReadOnlyList<RobocopyJob> jobs, IReadOnlyList<ScanTotals> totals)
    {
        if (jobs.Count != totals.Count)
            throw new ArgumentException("One scan total is needed per job.");
        _jobs = jobs;
        _totals = totals;
        Statuses = jobs.Select(_ => JobStatus.Pending).ToArray();
    }

    public JobStatus[] Statuses { get; }

    public event Action<int, JobStatus>? JobStatusChanged;
    public event Action<OverallProgress>? ProgressChanged;
    public event Action<string>? OutputLine;

    public ScanTotals GrandTotal => _totals.Aggregate(new ScanTotals(), (a, b) => a + b);

    public async Task<RunResult> RunAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new JobRunResult?[_jobs.Count];
        var grand = GrandTotal;
        var completed = new ScanTotals();
        var cancelled = false;

        for (var i = 0; i < _jobs.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                cancelled = true;
                SetStatus(i, JobStatus.Cancelled);
                continue;
            }

            SetStatus(i, JobStatus.Running);
            var jobTotal = _totals[i];
            var baseline = completed;
            var progress = new SyncProgress<JobProgress>(p =>
                ProgressChanged?.Invoke(new OverallProgress(
                    baseline.Files + Math.Min(p.FilesDone, jobTotal.Files),
                    grand.Files,
                    baseline.Bytes + Math.Min(p.BytesDone, jobTotal.Bytes),
                    grand.Bytes,
                    stopwatch.Elapsed,
                    p.CurrentFile)));

            JobRunResult result;
            try
            {
                result = await RobocopyRunner.RunAsync(_jobs[i], progress, line => OutputLine?.Invoke(line), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never leave a job stuck at Running: report it as failed and carry on with the next call.
                var message = $"Robocopy could not run: {ex.Message}";
                OutputLine?.Invoke(message);
                result = new JobRunResult(-1, false, new ExitCodeSummary(-1, false, true, message), null, [], 0, 0, TimeSpan.Zero);
            }
            results[i] = result;

            if (result.Cancelled)
            {
                cancelled = true;
                SetStatus(i, JobStatus.Cancelled);
                continue;
            }

            // A finished job has processed its whole source, including skipped files robocopy does not list.
            completed += jobTotal;
            SetStatus(i, result.Interpretation.IsSuccess ? JobStatus.Done : JobStatus.Failed);
            ProgressChanged?.Invoke(new OverallProgress(completed.Files, grand.Files, completed.Bytes, grand.Bytes, stopwatch.Elapsed, null));
        }

        return new RunResult(_jobs, results, cancelled, _jobs.Count > 0 && _jobs[0].IsDryRun, stopwatch.Elapsed);
    }

    private void SetStatus(int index, JobStatus status)
    {
        Statuses[index] = status;
        JobStatusChanged?.Invoke(index, status);
    }

    /// <summary>IProgress that invokes synchronously (callers marshal to the UI themselves).</summary>
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
