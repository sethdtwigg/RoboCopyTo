using System.Diagnostics;
using System.Windows;
using RoboCopyTo.Core.Logging;
using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && Cli.IsCliSwitch(args[0]))
            return Cli.Run(args[0]);

        // Double-clicked with no file or folder: act as the installer (install, or remove if installed).
        if (args.Length == 0)
            return SetupFlow.Run();

        ElevationJob? job = null;
        string? jobError = null;
        List<string> paths = [];
        App? app = null;
        MainWindow? window = null;

        if (args.Length >= 1 && string.Equals(args[0], "--job", StringComparison.OrdinalIgnoreCase))
        {
            // Elevated relaunch: read and verify the job (deleting the file) and skip the multi-select collector.
            // The job may sit in another account's profile when a different administrator approved UAC.
            try
            {
                if (args.Length < 3)
                    throw new InvalidOperationException("Usage: --job \"<job file>\" <sha256>. This switch is used internally by RoboCopyTo.");
                job = ElevationJob.ReadAndDelete(args[1], args[2]);
            }
            catch (Exception ex)
            {
                jobError = ex.Message;
            }
        }
        else if (args.Length > 0)
        {
            // Collect on a dedicated thread (it owns the mutex). Once this process knows it is the primary,
            // load WPF here in parallel, so the 500 ms quiet period is not added to startup time.
            var role = new ManualResetEventSlim();
            List<string>? collected = null;
            var finished = new ManualResetEventSlim();
            var collector = new Thread(() =>
            {
                try
                {
                    collected = SelectionCollector.Collect(args[0], role.Set);
                }
                finally
                {
                    finished.Set();
                }
            })
            { IsBackground = true, Name = "SelectionCollector" };
            collector.Start();

            WaitHandle.WaitAny([role.WaitHandle, finished.WaitHandle]);
            if (!role.IsSet && finished.IsSet && collected is null)
                return 0; // Handed our path to the primary process.

            app = CreateApp();
            window = new MainWindow();
            finished.Wait();
            if (collected is null)
                return 0;
            paths = collected;
        }

        // Housekeeping off the startup path.
        _ = Task.Run(() =>
        {
            try
            {
                LogManager.CreateDefault().DeleteExpiredLogs(DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        });

        app ??= CreateApp();
        if (jobError is not null)
        {
            MessageBox.Show("Could not read the elevated job: " + jobError, "RoboCopyTo", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }

        window ??= new MainWindow();
        window.Initialize(paths, job);
        if (Environment.GetEnvironmentVariable("ROBOCOPYTO_STARTUP_TIMING") is { Length: > 0 } timingFile)
        {
            // Developer diagnostics only (set the variable to a file path to record startup time).
            window.ContentRendered += (_, _) => File.AppendAllText(timingFile,
                $"{(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds:F0}{Environment.NewLine}");
        }
        return app.Run(window);
    }

    private static App CreateApp()
    {
        var app = new App();
        app.InitializeComponent();
        return app;
    }
}
