using Microsoft.Win32;

namespace RoboCopyTo.Core.Shell;

public sealed record ShellKeyStatus(string KeyPath, bool Exists, string? Command, string? ExePath);

/// <summary>
/// Reads and writes the Explorer context-menu entries. Only HKCU is touched, so no admin rights are needed.
/// </summary>
public sealed class ShellRegistration
{
    public const string MenuText = "RoboCopyTo...";
    public const string ClassesPath = @"Software\Classes";

    /// <summary>The two key trees, relative to HKCU\Software\Classes. Unregister deletes exactly these.</summary>
    public static readonly IReadOnlyList<string> KeyPaths = [@"*\shell\RoboCopyTo", @"Directory\shell\RoboCopyTo"];

    private readonly RegistryKey _classesRoot;
    private readonly string _displayRoot;

    /// <param name="classesRoot">Normally HKCU\Software\Classes; tests pass a scratch key.</param>
    public ShellRegistration(RegistryKey classesRoot, string displayRoot)
    {
        _classesRoot = classesRoot;
        _displayRoot = displayRoot;
    }

    public static ShellRegistration ForCurrentUser()
        => new(Registry.CurrentUser.CreateSubKey(ClassesPath, writable: true), @"HKCU\" + ClassesPath);

    public static string CommandFor(string exePath) => $"\"{exePath}\" \"%1\"";

    public void Register(string exePath)
    {
        foreach (var path in KeyPaths)
        {
            using var key = _classesRoot.CreateSubKey(path, writable: true);
            key.SetValue("", MenuText);
            key.SetValue("Icon", exePath);
            // Without Player, Explorer hides the item when more than 15 items are selected.
            key.SetValue("MultiSelectModel", "Player");
            using var command = key.CreateSubKey("command", writable: true);
            command.SetValue("", CommandFor(exePath));
        }
    }

    public void Unregister()
    {
        foreach (var path in KeyPaths)
            _classesRoot.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
    }

    public IReadOnlyList<ShellKeyStatus> GetStatus()
    {
        var result = new List<ShellKeyStatus>();
        foreach (var path in KeyPaths)
        {
            var display = _displayRoot + "\\" + path;
            using var key = _classesRoot.OpenSubKey(path);
            if (key is null)
            {
                result.Add(new(display, false, null, null));
                continue;
            }
            using var command = key.OpenSubKey("command");
            var cmd = command?.GetValue("") as string;
            result.Add(new(display, true, cmd, ExtractExePath(cmd)));
        }
        return result;
    }

    public static string? ExtractExePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;
        var c = command.Trim();
        if (c.StartsWith('"'))
        {
            var end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : null;
        }
        var space = c.IndexOf(' ');
        return space > 0 ? c[..space] : c;
    }
}
