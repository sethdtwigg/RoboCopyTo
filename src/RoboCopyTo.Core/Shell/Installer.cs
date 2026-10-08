namespace RoboCopyTo.Core.Shell;

public enum InstallState
{
    /// <summary>No installed copy and no context-menu entries.</summary>
    NotInstalled,

    /// <summary>An installed copy or the context-menu entries exist.</summary>
    Installed,
}

/// <param name="Deferred">Files and folders still in use (the running exe itself); delete them after this process exits.</param>
public sealed record UninstallResult(IReadOnlyList<string> Deferred);

/// <summary>
/// Per-user install: copies the exe and the DLLs beside it to %LocalAppData%\Programs\RoboCopyTo and
/// registers the context menu. Nothing needs administrator rights.
/// </summary>
public sealed class Installer
{
    public const string ExeName = "RoboCopyTo.exe";

    private readonly ShellRegistration _registration;

    public string InstallDirectory { get; }

    public string InstalledExe => Path.Combine(InstallDirectory, ExeName);

    public Installer(string installDirectory, ShellRegistration registration)
    {
        InstallDirectory = Path.GetFullPath(installDirectory);
        _registration = registration;
    }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "RoboCopyTo");

    public static Installer CreateDefault() => new(DefaultDirectory, ShellRegistration.ForCurrentUser());

    public InstallState GetState()
        => File.Exists(InstalledExe) || _registration.GetStatus().Any(s => s.Exists) ? InstallState.Installed : InstallState.NotInstalled;

    /// <summary>True when <paramref name="exePath"/> is the installed copy.</summary>
    public bool IsInstalledCopy(string exePath)
        => string.Equals(Path.GetFullPath(exePath), InstalledExe, StringComparison.OrdinalIgnoreCase);

    /// <summary>The exe plus the native DLLs published beside it.</summary>
    public static IReadOnlyList<string> FilesToInstall(string sourceExe)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(sourceExe))!;
        return [Path.GetFullPath(sourceExe), .. Directory.EnumerateFiles(dir, "*.dll").Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Copies the app into the install folder (replacing older files) and registers the context menu.</summary>
    /// <returns>The installed exe path.</returns>
    public string Install(string sourceExe)
    {
        Directory.CreateDirectory(InstallDirectory);
        foreach (var file in FilesToInstall(sourceExe))
        {
            var target = Path.Combine(InstallDirectory, Path.GetFileName(file));
            if (!string.Equals(Path.GetFullPath(file), target, StringComparison.OrdinalIgnoreCase))
                File.Copy(file, target, overwrite: true);
            RemoveDownloadMark(target);
        }
        _registration.Register(InstalledExe);
        return InstalledExe;
    }

    /// <summary>
    /// Removes the context-menu entries, then the installed exe and DLLs and the folder if it is left empty.
    /// Files that are in use are returned for deletion after exit.
    /// </summary>
    /// <param name="deferAllFiles">
    /// True when the installed copy is removing itself: delete no files now. A running .NET/WPF process may still
    /// load a DLL from its folder (vcruntime140_cor3.dll is needed at shutdown), so deleting any of them while it
    /// runs can crash it on exit.
    /// </param>
    public UninstallResult Uninstall(bool deferAllFiles = false)
    {
        _registration.Unregister();
        var deferred = new List<string>();
        if (Directory.Exists(InstallDirectory))
        {
            var files = new[] { InstalledExe }.Concat(Directory.EnumerateFiles(InstallDirectory, "*.dll"));
            foreach (var file in files.Where(File.Exists))
            {
                if (deferAllFiles)
                {
                    deferred.Add(file);
                    continue;
                }
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    deferred.Add(file);
                }
            }
            if (deferred.Count > 0)
                deferred.Add(InstallDirectory);
            else
                TryRemoveEmptyDirectory(InstallDirectory);
        }
        return new UninstallResult(deferred);
    }

    /// <summary>
    /// cmd.exe arguments that retry once a second, for up to 30 seconds, until the files can be deleted (that is,
    /// once this process has exited) and the folder is gone. The folder is removed only if it is empty; nothing
    /// is ever deleted recursively.
    /// </summary>
    public static string DeferredCleanupCommand(UninstallResult result)
    {
        var files = result.Deferred.Where(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();
        var dirs = result.Deferred.Except(files).ToList();
        var step = new List<string> { "ping 127.0.0.1 -n 2 >nul" };
        if (files.Count > 0)
            step.Add("del /f /q " + string.Join(" ", files.Select(f => $"\"{f}\"")) + " 2>nul");
        step.AddRange(dirs.Select(d => $"rmdir \"{d}\" 2>nul"));
        step.Add($"if not exist \"{(dirs.Count > 0 ? dirs[0] : files[0])}\" exit");
        return "/c for /l %i in (1,1,30) do @(" + string.Join(" & ", step) + ")";
    }

    /// <summary>Deletes logs/temp (%LocalAppData%\RoboCopyTo) and presets/settings (%AppData%\RoboCopyTo).</summary>
    public static void RemoveUserData()
    {
        foreach (var dir in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoboCopyTo"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoboCopyTo"),
                 })
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Files copied from another machine carry a Zone.Identifier stream that makes SmartScreen block them.</summary>
    private static void RemoveDownloadMark(string file)
    {
        try
        {
            File.Delete(file + ":Zone.Identifier");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }

    private static void TryRemoveEmptyDirectory(string dir)
    {
        try
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
