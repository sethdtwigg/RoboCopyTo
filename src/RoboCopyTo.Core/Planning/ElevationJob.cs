using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RoboCopyTo.Core.Options;
using RoboCopyTo.Core.Presets;
using RoboCopyTo.Core.Shell;

namespace RoboCopyTo.Core.Planning;

/// <summary>
/// What an elevated relaunch needs to run the copy the user started. Written to
/// %LocalAppData%\RoboCopyTo\Temp and deleted by the elevated process after reading.
/// </summary>
/// <remarks>
/// The writer passes the file's SHA-256 on the elevated command line, which no other process can change
/// after launch, so a job file swapped between writing and reading is rejected. The elevated process may be
/// a different account (a standard user approving UAC with an administrator's credentials); it then cannot
/// use its own %LocalAppData%, so any "...\RoboCopyTo\Temp\job-&lt;guid&gt;.json" path is accepted, provided no
/// part of it is a junction or symbolic link.
/// </remarks>
public sealed partial record ElevationJob(
    IReadOnlyList<string> Sources,
    string Destination,
    RobocopyOptions Options,
    bool DryRun,
    string? PresetName)
{
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoboCopyTo", "Temp");

    /// <summary>Copy of this job with mapped drive letters converted to UNC paths.</summary>
    public ElevationJob WithUncPaths(Func<string, string> toUnc)
        => this with { Sources = Sources.Select(toUnc).ToList(), Destination = toUnc(Destination) };

    public ElevationJob WithUncPaths() => WithUncPaths(MappedDrives.ToUnc);

    /// <returns>The job file path and the SHA-256 (hex) of its contents.</returns>
    public (string Path, string Hash) Write(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "job-" + Guid.NewGuid().ToString("N") + ".json");
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, JsonFiles.Options));
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            fs.Write(bytes);
        return (path, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    /// <summary>Reads, verifies, and deletes a job file.</summary>
    /// <exception cref="InvalidOperationException">The path is not an acceptable job file, or the contents do not match the hash.</exception>
    public static ElevationJob ReadAndDelete(string path, string expectedHash)
    {
        if (!IsAcceptablePath(path, out var reason))
            throw new InvalidOperationException(reason);
        var full = System.IO.Path.GetFullPath(path);
        try
        {
            var bytes = File.ReadAllBytes(full);
            var actual = Convert.ToHexString(SHA256.HashData(bytes));
            if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The job file was changed after RoboCopyTo wrote it, so it was not run.");
            var job = JsonSerializer.Deserialize<ElevationJob>(bytes, JsonFiles.Options)
                ?? throw new InvalidDataException("The job file is empty.");
            if (job.Sources is null || job.Destination is null || job.Options is null)
                throw new InvalidDataException("The job file is incomplete.");
            return job with { Options = job.Options.Sanitized() };
        }
        finally
        {
            try
            {
                File.Delete(full);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>"...\RoboCopyTo\Temp\job-&lt;32 hex&gt;.json", with no reparse point in the file, Temp or RoboCopyTo folder.</summary>
    public static bool IsAcceptablePath(string path, out string reason)
    {
        reason = "";
        string full;
        try
        {
            full = System.IO.Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = "The job file path is not valid.";
            return false;
        }
        var file = new FileInfo(full);
        var temp = file.Directory;
        var app = temp?.Parent;
        if (!JobFileName().IsMatch(file.Name)
            || temp is null || !temp.Name.Equals("Temp", StringComparison.OrdinalIgnoreCase)
            || app is null || !app.Name.Equals("RoboCopyTo", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Refusing to read {full}: RoboCopyTo only runs job files it wrote to its own Temp folder.";
            return false;
        }
        if (!file.Exists)
        {
            reason = $"The job file {full} no longer exists.";
            return false;
        }
        foreach (FileSystemInfo part in new FileSystemInfo[] { file, temp, app })
        {
            if (part.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                reason = $"Refusing to read {full}: {part.FullName} is a link or junction.";
                return false;
            }
        }
        return true;
    }

    [GeneratedRegex("^job-[0-9a-f]{32}\\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex JobFileName();
}
