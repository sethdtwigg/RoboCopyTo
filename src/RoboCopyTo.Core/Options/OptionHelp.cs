namespace RoboCopyTo.Core.Options;

/// <summary>Tooltip text for every option: the switch and one plain sentence.</summary>
public static class OptionHelp
{
    public const string IncludeSubfolders = "/E: Copy all subfolders too, including empty ones.";
    public const string IncludeSubfoldersLocked = "/E: Mirror always includes subfolders, so this stays on while Mirror is ticked.";
    public const string Mirror = "/MIR: Make each destination folder an exact copy of its source, deleting destination items that are not in the source.";
    public const string MirrorDisabled = "/MIR: Not available while individual files are selected, because mirroring a file's parent folder would delete everything else in the destination.";
    public const string Restartable = "/Z: Copy in restartable mode so an interrupted large file resumes where it stopped instead of starting over (slower).";
    public const string RestartableReplaced = "/Z: Replaced by /ZB (Restartable with backup fallback) in Advanced.";
    public const string Unbuffered = "/J: Copy without the Windows file cache, which is faster for very large files.";
    public const string Multithreaded = "/MT:n: Copy several files at once using n threads (1 to 128).";
    public const string Threads = "Number of threads for /MT (1 to 128).";

    public const string ExistingFiles = "What to do with files that already exist at the destination.";
    public const string CopyIfDifferent = "(no switch): Skip files with the same size and timestamp; copy everything else.";
    public const string OnlyNewer = "/XO: Never overwrite a newer destination file with an older one.";
    public const string SkipExisting = "/XC /XN /XO: Only copy files that are not at the destination yet.";
    public const string AlwaysOverwrite = "/IS /IT /IM: Copy everything, including files that look identical.";
    public const string Fft = "/FFT: The destination is FAT32 or exFAT, which stores times in 2-second steps; /FFT was added so unchanged files are not copied again.";

    public const string RetryCount = "/R:n: How many times to retry a file that fails to copy.";
    public const string RetryWait = "/W:n: How many seconds to wait between retries.";
    public const string NetworkFriendly = "/R:10 /W:15 /TBD: Retry longer and wait for a network share that has briefly disappeared. Turning it off restores your values.";

    public const string CopyPermissions = "/COPY:DATS: Also copy NTFS permissions (instead of the default /COPY:DAT).";
    public const string CopyOwner = "/COPY:...O: Also copy file ownership. Requires administrator rights.";
    public const string CopyAuditing = "/COPY:...U: Also copy auditing information. Requires administrator rights.";
    public const string BackupMode = "/B: Use backup mode to copy files you would otherwise be denied. Requires administrator rights.";
    public const string RestartableBackup = "/ZB: Restartable mode, falling back to backup mode when access is denied; replaces /Z. Requires administrator rights.";
    public const string SkipJunctions = "/XJ: Skip junctions and symbolic links instead of following them.";
    public const string KeepFolderTimestamps = "/DCOPY:DAT: Keep folder timestamps as well as data and attributes (robocopy's default is /DCOPY:DA).";
    public const string ExcludeFiles = "/XF: File names or wildcards to skip, separated by spaces. Quote names that contain spaces.";
    public const string ExcludeFolders = "/XD: Folder names or paths to skip, separated by spaces. Quote names that contain spaces.";
    public const string ExtraArguments = "Any other robocopy switches, appended to every call exactly as typed. Safety checks still apply.";

    public const string AppRequired = "Required by RoboCopyTo: /BYTES /FP /NP report exact sizes and full paths for the progress bar, /TEE keeps console output, and /UNILOG+ writes the run log.";
    public const string Elevation = "Requires administrator rights. Start will ask Windows for permission (UAC).";

    public const string PartialFileWarning = "Robocopy was stopped mid-copy without restartable mode (/Z), so a partially copied file may remain at the destination. Run the copy again to replace it.";
    public const string DryRunHeading = "Dry run (robocopy /L): listing what would be copied";
    public const string CopyHeading = "Copying";

    /// <summary>The warning to show after a cancel, or "" when none applies (dry run, or /Z or /ZB was on).</summary>
    public static string CancelWarning(RobocopyOptions options, bool dryRun)
        => dryRun || options.IsRestartable ? "" : PartialFileWarning;

    public static string ForExistingMode(ExistingFileMode mode) => mode switch
    {
        ExistingFileMode.OnlyNewer => OnlyNewer,
        ExistingFileMode.SkipExisting => SkipExisting,
        ExistingFileMode.AlwaysOverwrite => AlwaysOverwrite,
        _ => CopyIfDifferent,
    };
}
