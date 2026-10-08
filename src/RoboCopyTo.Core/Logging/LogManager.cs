using System.Globalization;
using System.Reflection;
using System.Text;

namespace RoboCopyTo.Core.Logging;

public sealed record LogHeader(
    DateTime Started,
    string AppVersion,
    IReadOnlyList<string> Sources,
    string Destination,
    IReadOnlyList<string> CommandLines,
    bool DryRun,
    bool Elevated = false);

/// <summary>
/// One log per run in %LocalAppData%\RoboCopyTo\Logs. The app writes a UTF-16LE header with a BOM;
/// robocopy then appends UTF-16LE via /UNILOG+. (Verified: when /UNILOG+ targets a file that does not
/// exist yet, robocopy writes 8-bit text, so the header must be written first.)
/// </summary>
public sealed class LogManager
{
    public const int RetentionDays = 30;
    public const string Extension = ".log";

    /// <summary>UTF-16LE with BOM, matching robocopy's /UNILOG output.</summary>
    public static readonly Encoding LogEncoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);

    public string Directory { get; }

    public LogManager(string directory) => Directory = directory;

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoboCopyTo", "Logs");

    public static LogManager CreateDefault() => new(DefaultDirectory);

    public static string AppVersion =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(LogManager).Assembly.GetName().Version?.ToString(3)
        ?? "unknown";

    public static string FileNameFor(DateTime started, bool dryRun)
        => started.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture) + (dryRun ? "_dryrun" : "") + Extension;

    /// <summary>A likely path for a new run's log, for the preview. Does not create the file; runs use <see cref="ReserveLogPath"/>.</summary>
    public string NewLogPath(DateTime started, bool dryRun)
    {
        var path = Path.Combine(Directory, FileNameFor(started, dryRun));
        var n = 2;
        while (File.Exists(path))
            path = Path.Combine(Directory, Path.GetFileNameWithoutExtension(FileNameFor(started, dryRun)) + "-" + n++ + Extension);
        return path;
    }

    /// <summary>
    /// Creates an empty log file with a unique name and returns its path. Creation is atomic (CreateNew), so
    /// two dialogs starting in the same second never share a log.
    /// </summary>
    public string ReserveLogPath(DateTime started, bool dryRun)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var stem = Path.GetFileNameWithoutExtension(FileNameFor(started, dryRun));
        for (var n = 1; ; n++)
        {
            var path = Path.Combine(Directory, (n == 1 ? stem : stem + "-" + n) + Extension);
            try
            {
                using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
                {
                }
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
                // Taken; try the next suffix.
            }
        }
    }

    public static string FormatHeader(LogHeader h)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RoboCopyTo run log" + (h.DryRun ? " (DRY RUN: nothing is copied)" : ""));
        sb.AppendLine("Date        : " + h.Started.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        sb.AppendLine("App version : " + h.AppVersion + (h.Elevated ? " (running as administrator)" : ""));
        sb.AppendLine("Destination : " + h.Destination);
        sb.AppendLine("Sources     :");
        foreach (var s in h.Sources)
            sb.AppendLine("  " + s);
        sb.AppendLine("Commands    :");
        for (var i = 0; i < h.CommandLines.Count; i++)
            sb.AppendLine($"  [{i + 1}] {h.CommandLines[i]}");
        sb.AppendLine(new string('=', 79));
        return sb.ToString();
    }

    /// <summary>Creates the log with the header, UTF-16LE with BOM.</summary>
    public void WriteHeader(string path, LogHeader header)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, FormatHeader(header), LogEncoding);
    }

    /// <summary>Appends a note (no BOM) once robocopy is no longer writing.</summary>
    public static void AppendNote(string path, string text)
    {
        var bytes = new UnicodeEncoding(false, false).GetBytes(Environment.NewLine + text + Environment.NewLine);
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        fs.Write(bytes);
    }

    public static bool IsExpired(DateTime lastWriteUtc, DateTime nowUtc) => nowUtc - lastWriteUtc > TimeSpan.FromDays(RetentionDays);

    /// <summary>Deletes .log files older than 30 days in the log folder only (not subfolders).</summary>
    public int DeleteExpiredLogs(DateTime nowUtc)
    {
        if (!System.IO.Directory.Exists(Directory))
            return 0;
        var deleted = 0;
        foreach (var file in new DirectoryInfo(Directory).EnumerateFiles("*", SearchOption.TopDirectoryOnly))
        {
            if (!string.Equals(file.Extension, Extension, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!IsExpired(file.LastWriteTimeUtc, nowUtc))
                continue;
            try
            {
                file.Delete();
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use or protected; try again next launch.
            }
        }
        return deleted;
    }
}
