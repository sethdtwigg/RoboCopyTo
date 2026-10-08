using System.Runtime.InteropServices;

namespace RoboCopyTo.Core.Planning;

/// <summary>Facts about the destination volume: file system (for /FFT) and free space.</summary>
public static partial class VolumeInfo
{
    /// <summary>FAT32 and exFAT store timestamps in 2-second steps, so robocopy needs /FFT.</summary>
    public static bool IsFatFamily(string? driveFormat)
        => driveFormat is not null && (driveFormat.Equals("FAT32", StringComparison.OrdinalIgnoreCase)
            || driveFormat.Equals("exFAT", StringComparison.OrdinalIgnoreCase)
            || driveFormat.Equals("FAT", StringComparison.OrdinalIgnoreCase)
            || driveFormat.Equals("FAT16", StringComparison.OrdinalIgnoreCase)
            || driveFormat.Equals("FAT12", StringComparison.OrdinalIgnoreCase));

    /// <summary>The destination volume's file system name (DriveInfo.DriveFormat), or null if unknown (e.g. a network share).</summary>
    public static string? GetDriveFormat(string destination)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(destination));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
                return null;
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.DriveFormat : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool IsFatDestination(string destination) => IsFatFamily(GetDriveFormat(destination));

    /// <summary>Free bytes available to the user at the destination (works for UNC), or null if unknown.</summary>
    public static long? GetFreeBytes(string destination)
    {
        try
        {
            var dir = Path.GetFullPath(destination);
            while (!Directory.Exists(dir))
            {
                var parent = Path.GetDirectoryName(dir);
                if (parent is null)
                    return null;
                dir = parent;
            }
            if (!dir.EndsWith('\\'))
                dir += "\\";
            return GetDiskFreeSpaceExW(dir, out var available, out _, out _) ? (long)available : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceExW(string directory, out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes);
}
