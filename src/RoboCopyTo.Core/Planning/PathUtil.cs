namespace RoboCopyTo.Core.Planning;

/// <summary>
/// Path normalization, comparison, and robocopy quoting.
/// </summary>
/// <remarks>
/// Quoting rules were determined against real robocopy (see PathQuotingIntegrationTests):
/// <list type="bullet">
/// <item>"C:\dir\" breaks parsing: the trailing backslash escapes the closing quote. Non-root paths are emitted without a trailing backslash.</item>
/// <item>A drive root must keep its backslash, so it is doubled: "E:\\". ("E:" alone means the current directory on E.)</item>
/// <item>A UNC share root is emitted without a trailing backslash: "\\server\share".</item>
/// </list>
/// </remarks>
public static class PathUtil
{
    /// <summary>Full path, upper-case drive letter, no trailing backslash except on a drive root.</summary>
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var p = StripLongPathPrefix(path.Trim());
        if (IsBareDrive(p))
            p += "\\";
        p = Path.GetFullPath(p).Replace('/', '\\');
        if (p.Length >= 2 && p[1] == ':' && char.IsLower(p[0]))
            p = char.ToUpperInvariant(p[0]) + p[1..];
        p = p.TrimEnd('\\');
        if (IsBareDrive(p))
            p += "\\";
        return p;
    }

    public static bool IsDriveRoot(string path)
    {
        var p = path.Trim();
        if (IsBareDrive(p))
            return true;
        return p.Length == 3 && IsBareDrive(p[..2]) && (p[2] == '\\' || p[2] == '/');
    }

    public static bool IsUncShareRoot(string path)
    {
        var p = path.Trim().Replace('/', '\\');
        if (!p.StartsWith(@"\\", StringComparison.Ordinal) || p.StartsWith(@"\\?\", StringComparison.Ordinal))
            return false;
        var parts = p[2..].TrimEnd('\\').Split('\\');
        return parts.Length == 2 && parts.All(s => s.Length > 0);
    }

    public static bool AreSame(string a, string b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="child"/> lies below <paramref name="parent"/> (not equal to it).</summary>
    public static bool IsStrictlyInside(string child, string parent)
    {
        var c = Normalize(child);
        var p = Normalize(parent);
        var prefix = p.EndsWith('\\') ? p : p + "\\";
        return c.Length > prefix.Length - 1
            && c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(c, p, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSameOrInside(string child, string parent) => AreSame(child, parent) || IsStrictlyInside(child, parent);

    /// <summary>The last path segment; for a UNC share root, the share name.</summary>
    public static string GetFolderName(string path)
    {
        var n = Normalize(path);
        if (IsUncShareRoot(n))
            return n[(n.LastIndexOf('\\') + 1)..];
        return Path.GetFileName(n);
    }

    /// <summary>Quotes a path in the form robocopy parses correctly.</summary>
    public static string Quote(string path)
    {
        if (path.Contains('"'))
            throw new ArgumentException("Windows paths cannot contain a double quote.", nameof(path));
        var n = Normalize(path);
        // Only a drive root still ends in a backslash here; double it so it does not escape the quote.
        if (n.EndsWith('\\'))
            n += "\\";
        return "\"" + n + "\"";
    }

    /// <summary>Quotes a non-path argument (file name, pattern) when it contains spaces.</summary>
    public static string QuoteIfNeeded(string value)
        => value.Length == 0 || value.Any(char.IsWhiteSpace) ? "\"" + value + "\"" : value;

    /// <summary>"\\?\C:\x" → "C:\x" and "\\?\UNC\srv\share" → "\\srv\share" (robocopy rejects the prefixed forms).</summary>
    public static string StripLongPathPrefix(string p)
    {
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + p[8..];
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal))
            return p[4..];
        return p;
    }

    private static bool IsBareDrive(string p) => p.Length == 2 && p[1] == ':' && char.IsAsciiLetter(p[0]);
}
