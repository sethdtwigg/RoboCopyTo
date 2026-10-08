using System.Runtime.InteropServices;
using RoboCopyTo.Core.Shell;

namespace RoboCopyTo.App;

/// <summary>--register, --unregister and --status. Output goes to the console that started the exe.</summary>
internal static partial class Cli
{
    public static bool IsCliSwitch(string arg) => arg.ToLowerInvariant() is "--register" or "--unregister" or "--status" or "--help" or "-h" or "/?";

    public static int Run(string arg)
    {
        AttachConsole(AttachParentProcess);
        Console.WriteLine();
        try
        {
            var reg = ShellRegistration.ForCurrentUser();
            switch (arg.ToLowerInvariant())
            {
                case "--register":
                    var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the exe path.");
                    reg.Register(exe);
                    Console.WriteLine($"Registered RoboCopyTo for files and folders, pointing at:\n  {exe}");
                    Console.WriteLine("Right-click a file or folder and choose Show more options > RoboCopyTo...");
                    PrintStatus(reg);
                    return 0;
                case "--unregister":
                    reg.Unregister();
                    Console.WriteLine("Removed the RoboCopyTo context-menu entries.");
                    PrintStatus(reg);
                    return 0;
                case "--status":
                    return PrintStatus(reg) ? 0 : 1;
                default:
                    PrintHelp();
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return 2;
        }
        finally
        {
            // Nudge the parent shell to redraw its prompt after we wrote below it.
            Console.Out.Flush();
        }
    }

    /// <returns>True when both keys exist.</returns>
    private static bool PrintStatus(ShellRegistration reg)
    {
        var all = true;
        foreach (var s in reg.GetStatus())
        {
            all &= s.Exists;
            Console.WriteLine(s.Exists
                ? $"  [present] {s.KeyPath}\n            -> {s.ExePath}{(s.ExePath is not null && !File.Exists(s.ExePath) ? "  (file not found!)" : "")}"
                : $"  [missing] {s.KeyPath}");
        }
        return all;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            RoboCopyTo - build and run robocopy copies from the Explorer context menu.

              RoboCopyTo.exe                (no arguments) Install for this user, or remove if already installed
              RoboCopyTo.exe --register     Add "RoboCopyTo..." to the context menu (current user, no admin needed)
              RoboCopyTo.exe --unregister   Remove the context-menu entries
              RoboCopyTo.exe --status       Show whether the entries exist and which exe they point to
              RoboCopyTo.exe <path>         Open the dialog for a file or folder (what Explorer runs)
            """);
    }

    private const int AttachParentProcess = -1;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);
}
