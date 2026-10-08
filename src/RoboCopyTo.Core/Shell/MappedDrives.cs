using System.Runtime.InteropServices;

namespace RoboCopyTo.Core.Shell;

/// <summary>
/// Converts mapped drive letters to UNC paths. Elevated processes usually cannot see the user's mapped drives.
/// </summary>
public static partial class MappedDrives
{
    private const int NoError = 0;
    private const int ErrorMoreData = 234;

    /// <summary>Returns the UNC form of a path on a mapped drive, or the path unchanged.</summary>
    public static string ToUnc(string path) => ToUnc(path, GetConnection);

    /// <summary>Testable core: <paramref name="lookup"/> maps "X:" to "\\server\share" or null.</summary>
    public static string ToUnc(string path, Func<string, string?> lookup)
    {
        if (path.Length < 2 || path[1] != ':' || !char.IsAsciiLetter(path[0]))
            return path;
        var remote = lookup(char.ToUpperInvariant(path[0]) + ":");
        if (string.IsNullOrEmpty(remote))
            return path;
        var rest = path[2..].TrimStart('\\', '/');
        return rest.Length == 0 ? remote.TrimEnd('\\') : remote.TrimEnd('\\') + "\\" + rest;
    }

    /// <summary>The remote name for a drive like "Z:", or null when it is not a network mapping.</summary>
    public static string? GetConnection(string drive)
    {
        var length = 512;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var buffer = new char[length];
            var result = WNetGetConnectionW(drive, buffer, ref length);
            if (result == NoError)
                return new string(buffer, 0, Array.IndexOf(buffer, '\0') is var end and >= 0 ? end : buffer.Length);
            if (result != ErrorMoreData)
                return null;
        }
        return null;
    }

    [LibraryImport("mpr.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WNetGetConnectionW(string localName, [Out] char[] remoteName, ref int length);
}
