using System.Diagnostics;
using RoboCopyTo.Core.Logging;
using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.Core.Execution;

public sealed record JobProgress(long FilesDone, long BytesDone, string? CurrentFile);

public sealed record JobRunResult(
    int ExitCode,
    bool Cancelled,
    ExitCodeSummary Interpretation,
    RobocopySummary? Summary,
    IReadOnlyList<FailedItem> Failures,
    long FilesDone,
    long BytesDone,
    TimeSpan Elapsed);

/// <summary>Runs one robocopy call and reports progress and output.</summary>
/// <remarks>
/// Progress is parsed live from stdout (decoded with the OEM code page). Display text and the failed-file
/// list come from the run's UTF-16 log, which robocopy appends to via /UNILOG+, because stdout cannot
/// represent characters outside the OEM code page (they arrive as '?').
/// </remarks>
public static class RobocopyRunner
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan TailInterval = TimeSpan.FromMilliseconds(200);

    private static readonly AsyncLocal<string?> PathOverride = new();

    /// <summary>Tests point this elsewhere to exercise start failures. Async-local, so it never leaks into other tests.</summary>
    internal static string? RobocopyPathOverride { get => PathOverride.Value; set => PathOverride.Value = value; }

    public static string RobocopyPath => RobocopyPathOverride ?? Path.Combine(Environment.SystemDirectory, "robocopy.exe");

    /// <param name="onOutputLine">Receives each display line (from the log; from stdout if the log cannot be read).</param>
    public static async Task<JobRunResult> RunAsync(
        RobocopyJob job,
        IProgress<JobProgress>? progress = null,
        Action<string>? onOutputLine = null,
        CancellationToken ct = default)
    {
        var stdoutParser = new OutputParser();
        var logParser = new OutputParser();
        // The app writes the log header before running; without it, fall back to stdout for display.
        var logUsable = File.Exists(job.LogPath);
        if (onOutputLine is not null)
        {
            if (logUsable)
                logParser.LineParsed += onOutputLine;
            else
                stdoutParser.LineParsed += onOutputLine;
        }

        using var tailer = new LogTailer(job.LogPath, LogTailer.CurrentLength(job.LogPath));
        var stopwatch = Stopwatch.StartNew();

        var psi = new ProcessStartInfo(RobocopyPath, job.Arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = ConsoleEncoding.Oem,
            StandardErrorEncoding = ConsoleEncoding.Oem,
        };

        if (ct.IsCancellationRequested)
            return new JobRunResult(-1, true, new ExitCodeSummary(-1, false, false, "Cancelled"), null, [], 0, 0, TimeSpan.Zero);

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            var message = $"Robocopy could not be started: {ex.Message}";
            onOutputLine?.Invoke(message);
            return new JobRunResult(-1, false, new ExitCodeSummary(-1, false, true, message), null, [], 0, 0, stopwatch.Elapsed);
        }
        _ = process.StandardError.ReadToEndAsync(CancellationToken.None);

        // Only a kill that actually stopped a running robocopy counts as a cancel; if it had already
        // finished, its real exit code and summary are reported.
        var cancelled = false;
        using var killOnCancel = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    cancelled = true;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Exited between the check and the kill.
            }
        });

        var readStdout = Task.Run(async () =>
        {
            var buffer = new char[8192];
            var lastReport = Stopwatch.StartNew();
            int n;
            while ((n = await process.StandardOutput.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false)) > 0)
            {
                lock (stdoutParser)
                    stdoutParser.FeedText(new string(buffer, 0, n));
                if (lastReport.Elapsed >= ProgressInterval)
                {
                    lastReport.Restart();
                    Report(progress, stdoutParser);
                }
            }
        }, CancellationToken.None);

        var tail = Task.Run(async () =>
        {
            while (logUsable && !process.HasExited)
            {
                logParser.FeedText(tailer.ReadNew());
                try
                {
                    await Task.Delay(TailInterval, CancellationToken.None).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                }
            }
        }, CancellationToken.None);

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        await readStdout.ConfigureAwait(false);
        await tail.ConfigureAwait(false);

        // Robocopy has exited and closed the log; read whatever is left.
        if (logUsable)
            logParser.FeedText(tailer.ReadNew());
        logParser.Complete();
        lock (stdoutParser)
            stdoutParser.Complete();
        Report(progress, stdoutParser);



        var exitCode = cancelled ? -1 : process.ExitCode;
        var interpretation = cancelled
            ? new ExitCodeSummary(exitCode, false, false, "Cancelled")
            : ExitCodeInterpreter.Interpret(exitCode, job.DeletesExtras, job.IsDryRun);

        return new JobRunResult(
            exitCode,
            cancelled,
            interpretation,
            (logUsable ? logParser.Summary : null) ?? stdoutParser.Summary,
            logUsable ? logParser.Failures : stdoutParser.Failures,
            stdoutParser.FilesDone,
            stdoutParser.BytesDone,
            stopwatch.Elapsed);
    }

    private static void Report(IProgress<JobProgress>? progress, OutputParser parser)
    {
        if (progress is null)
            return;
        JobProgress p;
        lock (parser)
            p = new JobProgress(parser.FilesDone, parser.BytesDone, parser.CurrentFile);
        progress.Report(p);
    }
}
