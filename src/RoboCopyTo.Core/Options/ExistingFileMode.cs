namespace RoboCopyTo.Core.Options;

/// <summary>How robocopy treats files that already exist at the destination.</summary>
public enum ExistingFileMode
{
    /// <summary>Skip files with the same size and timestamp; copy everything else. No switch.</summary>
    CopyIfDifferent,

    /// <summary>Never overwrite a newer destination file with an older one. /XO</summary>
    OnlyNewer,

    /// <summary>Only copy files not yet at the destination. /XC /XN /XO</summary>
    SkipExisting,

    /// <summary>Copy everything, including identical files. /IS /IT /IM (/IM covers files that differ only in NTFS change time).</summary>
    AlwaysOverwrite,
}

public static class ExistingFileModeExtensions
{
    public static IReadOnlyList<string> Switches(this ExistingFileMode mode) => mode switch
    {
        ExistingFileMode.OnlyNewer => ["/XO"],
        ExistingFileMode.SkipExisting => ["/XC", "/XN", "/XO"],
        ExistingFileMode.AlwaysOverwrite => ["/IS", "/IT", "/IM"],
        _ => [],
    };

    public static string DisplayName(this ExistingFileMode mode) => mode switch
    {
        ExistingFileMode.OnlyNewer => "Only copy newer",
        ExistingFileMode.SkipExisting => "Skip existing",
        ExistingFileMode.AlwaysOverwrite => "Always overwrite",
        _ => "Copy if different",
    };
}
