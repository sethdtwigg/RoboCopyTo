using System.Diagnostics;
using System.Windows;
using RoboCopyTo.Core.Shell;

namespace RoboCopyTo.App;

/// <summary>
/// What double-clicking RoboCopyTo.exe (no arguments) does: install it for the current user, or remove it if
/// it is already installed. Every step asks first.
/// </summary>
internal static class SetupFlow
{
    private const string Title = "RoboCopyTo";

    public static int Run()
    {
        var installer = Installer.CreateDefault();
        var self = Environment.ProcessPath!;
        try
        {
            return installer.GetState() == InstallState.Installed ? Remove(installer) : Install(installer, self);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                "Setup could not finish:\n\n" + ex.Message + "\n\nIf RoboCopyTo is open, close it and try again.",
                Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static int Install(Installer installer, string self)
    {
        var answer = MessageBox.Show(
            $"Install RoboCopyTo for your user account?\n\n" +
            $"It will be copied to {installer.InstallDirectory} and \"RoboCopyTo...\" will be added to the Explorer " +
            "context menu (right-click > Show more options). No administrator rights are needed.",
            Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes)
            return 0;

        installer.Install(self);
        MessageBox.Show(
            "RoboCopyTo is installed.\n\nRight-click any file or folder and choose Show more options > RoboCopyTo...\n\n" +
            "To remove it, run RoboCopyTo.exe again.",
            Title, MessageBoxButton.OK, MessageBoxImage.Information);
        return 0;
    }

    private static int Remove(Installer installer)
    {
        var others = OtherInstalledInstances(installer);
        if (others > 0)
        {
            MessageBox.Show($"RoboCopyTo is open ({others} window{(others == 1 ? "" : "s")}). Close it, then run RoboCopyTo.exe again to remove it.",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return 1;
        }

        var answer = MessageBox.Show(
            $"RoboCopyTo is installed in {installer.InstallDirectory}.\n\nRemove it and its context-menu entry?",
            Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return 0;

        var removeData = MessageBox.Show(
            "Also delete your RoboCopyTo logs, presets and settings?",
            Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

        // When this is the installed copy, delete nothing while it runs: WPF still loads DLLs from this folder at
        // shutdown. A hidden cmd.exe finishes the job once this process has exited.
        var result = installer.Uninstall(deferAllFiles: installer.IsInstalledCopy(Environment.ProcessPath!));
        if (removeData)
            Installer.RemoveUserData();

        MessageBox.Show("RoboCopyTo has been removed.", Title, MessageBoxButton.OK, MessageBoxImage.Information);

        if (result.Deferred.Count > 0)
        {
            Process.Start(new ProcessStartInfo("cmd.exe", Installer.DeferredCleanupCommand(result))
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(),
            });
        }
        return 0;
    }

    /// <summary>Dialogs still running from the installed folder (other than this process).</summary>
    private static int OtherInstalledInstances(Installer installer)
    {
        var me = Environment.ProcessId;
        return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Installer.ExeName)).Count(p =>
        {
            try
            {
                return p.Id != me && p.MainModule?.FileName is { } path && installer.IsInstalledCopy(path);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return false;
            }
        });
    }
}
