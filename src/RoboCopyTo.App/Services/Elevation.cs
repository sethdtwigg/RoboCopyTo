using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace RoboCopyTo.App.Services;

internal enum RelaunchOutcome { Started, Declined, Failed }

internal static class Elevation
{
    private const int ErrorCancelled = 1223;

    public static bool IsElevated { get; } = CheckElevated();

    private static bool CheckElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Relaunches this exe elevated with --job "&lt;path&gt;" &lt;sha256&gt;. The hash travels on the command line,
    /// which no other process can alter, so the elevated process can detect a swapped job file.
    /// </summary>
    public static RelaunchOutcome TryRelaunch(string jobPath, string hash, out string? error)
    {
        error = null;
        var psi = new ProcessStartInfo(Environment.ProcessPath!, $"--job \"{jobPath}\" {hash}")
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        try
        {
            using var _ = Process.Start(psi);
            return RelaunchOutcome.Started;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return RelaunchOutcome.Declined;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            error = ex.Message;
            return RelaunchOutcome.Failed;
        }
    }
}
