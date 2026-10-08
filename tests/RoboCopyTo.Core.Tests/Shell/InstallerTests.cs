using System.Diagnostics;
using Microsoft.Win32;
using RoboCopyTo.Core.Shell;
using RoboCopyTo.Core.Tests.Integration;

namespace RoboCopyTo.Core.Tests.Shell;

/// <summary>Temp folders and a scratch registry key under HKCU\Software\RoboCopyTo.Tests only.</summary>
public sealed class InstallerTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly string _scratchPath = @"Software\RoboCopyTo.Tests\" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _root;
    private readonly ShellRegistration _reg;
    private readonly Installer _installer;
    private readonly string _sourceExe;

    public InstallerTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_scratchPath, writable: true);
        _reg = new ShellRegistration(_root, "TEST");
        _installer = new Installer(_t[@"Programs\RoboCopyTo"], _reg);
        // A fake "publish" folder: the exe and the native DLLs beside it.
        _sourceExe = _t.File(@"publish\RoboCopyTo.exe", "exe");
        _t.File(@"publish\wpfgfx_cor3.dll", "dll1");
        _t.File(@"publish\PenImc_cor3.dll", "dll2");
        _t.File(@"publish\notes.txt", "not installed");
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_scratchPath, throwOnMissingSubKey: false);
        using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\RoboCopyTo.Tests", writable: true))
            if (parent is { SubKeyCount: 0, ValueCount: 0 })
                Registry.CurrentUser.DeleteSubKey(@"Software\RoboCopyTo.Tests", throwOnMissingSubKey: false);
        _t.Dispose();
    }

    [Fact]
    public void Fresh_machine_is_not_installed()
        => Assert.Equal(InstallState.NotInstalled, _installer.GetState());

    [Fact]
    public void Install_copies_exe_and_dlls_and_registers_the_installed_exe()
    {
        var installed = _installer.Install(_sourceExe);

        Assert.Equal(_installer.InstalledExe, installed);
        Assert.Equal(["PenImc_cor3.dll", "RoboCopyTo.exe", "wpfgfx_cor3.dll"],
            Directory.GetFiles(_installer.InstallDirectory).Select(Path.GetFileName).Order());
        Assert.All(_reg.GetStatus(), s => Assert.Equal(installed, s.ExePath));
        Assert.Equal(InstallState.Installed, _installer.GetState());
        Assert.True(_installer.IsInstalledCopy(installed.ToUpperInvariant()));
        Assert.False(_installer.IsInstalledCopy(_sourceExe));
    }

    [Fact]
    public void Install_removes_the_downloaded_from_internet_mark()
    {
        File.WriteAllText(_sourceExe + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        _installer.Install(_sourceExe);
        Assert.False(File.Exists(_installer.InstalledExe + ":Zone.Identifier"));
    }

    [Fact]
    public void Install_again_replaces_older_files()
    {
        _installer.Install(_sourceExe);
        File.WriteAllText(_sourceExe, "exe v2");
        _installer.Install(_sourceExe);
        Assert.Equal("exe v2", File.ReadAllText(_installer.InstalledExe));
    }

    [Fact]
    public void Running_the_installed_copy_to_install_does_not_copy_onto_itself()
    {
        _installer.Install(_sourceExe);
        _installer.Install(_installer.InstalledExe);
        Assert.Equal("exe", File.ReadAllText(_installer.InstalledExe));
    }

    [Fact]
    public void Leftover_menu_entries_alone_count_as_installed()
    {
        _reg.Register(@"C:\gone\RoboCopyTo.exe");
        Assert.Equal(InstallState.Installed, _installer.GetState());
        Assert.Empty(_installer.Uninstall().Deferred);
        Assert.Equal(InstallState.NotInstalled, _installer.GetState());
    }

    [Fact]
    public void Uninstall_removes_entries_files_and_the_empty_folder()
    {
        _installer.Install(_sourceExe);
        var result = _installer.Uninstall();

        Assert.Empty(result.Deferred);
        Assert.All(_reg.GetStatus(), s => Assert.False(s.Exists));
        Assert.False(Directory.Exists(_installer.InstallDirectory));
        Assert.Equal(InstallState.NotInstalled, _installer.GetState());
        Assert.True(File.Exists(_sourceExe), "the source copy is untouched");
    }

    [Fact]
    public void Uninstall_keeps_files_it_did_not_install()
    {
        _installer.Install(_sourceExe);
        var mine = Path.Combine(_installer.InstallDirectory, "my notes.txt");
        File.WriteAllText(mine, "keep");

        _installer.Uninstall();

        Assert.True(File.Exists(mine));
        Assert.False(File.Exists(_installer.InstalledExe));
    }

    [Fact]
    public void Files_in_use_are_deferred_and_the_cleanup_command_removes_them_after_release()
    {
        _installer.Install(_sourceExe);
        UninstallResult result;
        using (new FileStream(_installer.InstalledExe, FileMode.Open, FileAccess.Read, FileShare.Read)) // like a running exe
            result = _installer.Uninstall();

        Assert.Equal([_installer.InstalledExe, _installer.InstallDirectory], result.Deferred);
        Assert.True(File.Exists(_installer.InstalledExe));

        var command = Installer.DeferredCleanupCommand(result);
        Assert.Contains($"\"{_installer.InstalledExe}\"", command);
        Assert.Contains($"rmdir \"{_installer.InstallDirectory}\"", command);
        Assert.DoesNotContain("/s ", command);

        RunCleanup(command);
        Assert.False(Directory.Exists(_installer.InstallDirectory));
    }

    [Fact]
    public void Self_removal_deletes_nothing_while_running_then_cleanup_removes_everything()
    {
        _installer.Install(_sourceExe);

        var result = _installer.Uninstall(deferAllFiles: true);

        Assert.All(_reg.GetStatus(), s => Assert.False(s.Exists));
        Assert.Equal(3, Directory.GetFiles(_installer.InstallDirectory).Length); // nothing deleted yet
        Assert.Equal(4, result.Deferred.Count); // exe, 2 DLLs, folder

        RunCleanup(Installer.DeferredCleanupCommand(result));
        Assert.False(Directory.Exists(_installer.InstallDirectory));
    }

    [Fact]
    public void Cleanup_waits_for_a_file_still_in_use_and_never_removes_other_files()
    {
        _installer.Install(_sourceExe);
        var result = _installer.Uninstall(deferAllFiles: true);
        var mine = Path.Combine(_installer.InstallDirectory, "my notes.txt");
        File.WriteAllText(mine, "keep");

        using var p = Process.Start(new ProcessStartInfo("cmd.exe", Installer.DeferredCleanupCommand(result)) { CreateNoWindow = true, UseShellExecute = false })!;
        using (new FileStream(_installer.InstalledExe, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        {
            Thread.Sleep(2500); // the "running exe" holds its file for a while; cleanup keeps retrying
        }
        // Only the exe and DLLs go; the folder stays because it still holds the user's file.
        Assert.True(SpinWait.SpinUntil(() => !File.Exists(_installer.InstalledExe), 10_000));
        Assert.True(File.Exists(mine));
        p.Kill();
    }

    private static void RunCleanup(string command)
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", command) { CreateNoWindow = true, UseShellExecute = false })!;
        Assert.True(p.WaitForExit(40_000), "cleanup should finish");
    }

    [Fact]
    public void Default_directory_is_under_local_app_data_programs()
        => Assert.EndsWith(@"AppData\Local\Programs\RoboCopyTo", Installer.DefaultDirectory, StringComparison.OrdinalIgnoreCase);
}
