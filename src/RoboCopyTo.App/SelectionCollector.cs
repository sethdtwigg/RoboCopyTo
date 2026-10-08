using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace RoboCopyTo.App;

/// <summary>
/// Explorer starts one process per selected item. The first process (primary) owns a named mutex and
/// gathers the other processes' paths over a named pipe until 500 ms pass without a new one.
/// </summary>
internal static class SelectionCollector
{
    public const string MutexName = @"Local\RoboCopyTo.Collect";
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(500);
    private const int ConnectTimeoutMs = 2000;
    private const int RetryDelayMs = 200;
    private const int MaxAttempts = 5;
    private const int ListeningInstances = 4;

    public static string PipeName => "RoboCopyTo.Collect." + Process.GetCurrentProcess().SessionId;

    /// <summary>
    /// Returns the collected paths if this process should open the dialog, or null if it handed its
    /// path to a primary and should exit. <paramref name="onBecamePrimary"/> runs as soon as this process
    /// owns the mutex, so the caller can start loading the UI during the 500 ms quiet period.
    /// </summary>
    /// <remarks>The mutex is acquired and released on the calling thread, as Win32 mutexes require.</remarks>
    public static List<string>? Collect(string ownPath, Action? onBecamePrimary = null)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (createdNew)
            {
                onBecamePrimary?.Invoke();
                return RunPrimary(mutex, ownPath);
            }

            mutex.Dispose();
            if (TrySend(ownPath))
                return null;
            Thread.Sleep(RetryDelayMs);
        }
        // Could not reach a primary: become one with just this path.
        return [ownPath];
    }

    private static List<string> RunPrimary(Mutex mutex, string ownPath)
    {
        var paths = new List<string> { ownPath };
        var gate = new object();
        var active = 0;
        var lastArrival = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource();

        var listeners = Enumerable.Range(0, ListeningInstances)
            .Select(_ => Task.Run(() => ListenAsync(cts.Token,
                onConnected: () =>
                {
                    lock (gate)
                        active++;
                },
                onDone: path =>
                {
                    lock (gate)
                    {
                        if (path is not null)
                            paths.Add(path);
                        active--;
                        lastArrival.Restart();
                    }
                })))
            .ToArray();

        // Quiet means: no connection mid-read and 500 ms since the last path arrived.
        while (true)
        {
            TimeSpan wait;
            lock (gate)
                wait = active > 0 ? TimeSpan.FromMilliseconds(20) : QuietPeriod - lastArrival.Elapsed;
            if (wait <= TimeSpan.Zero)
                break;
            Thread.Sleep(wait);
        }

        // Spec order: release the mutex (so a later right-click starts a new primary), then stop the server.
        mutex.ReleaseMutex();
        mutex.Dispose();
        cts.Cancel();
        // A client that connected in the gap between those two steps is still read to the end (reads are not
        // cancellable and time out after 2 s), so wait for every listener before taking the final list;
        // otherwise its path could be lost after that client has already exited believing it was delivered.
        try
        {
            Task.WaitAll(listeners, TimeSpan.FromMilliseconds(ConnectTimeoutMs + 500));
        }
        catch (AggregateException)
        {
        }
        lock (gate)
            return [.. paths];
    }

    private static async Task ListenAsync(CancellationToken ct, Action onConnected, Action<string?> onDone)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(PipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                return;
            }
            catch (IOException)
            {
                server?.Dispose();
                continue;
            }

            // Once connected, always finish reading (not cancellable) so an accepted path is never lost.
            onConnected();
            string? path = null;
            try
            {
                using var reader = new StreamReader(server, new UTF8Encoding(false));
                using var readTimeout = new CancellationTokenSource(ConnectTimeoutMs);
                var line = await reader.ReadLineAsync(readTimeout.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(line))
                    path = line.Trim();
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                // The client disconnected or stalled; keep listening.
            }
            finally
            {
                server.Dispose();
                onDone(path);
            }
        }
    }

    private static bool TrySend(string path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(ConnectTimeoutMs);
            var bytes = new UTF8Encoding(false).GetBytes(path + "\n");
            client.Write(bytes);
            client.Flush();
            client.WaitForPipeDrain();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
