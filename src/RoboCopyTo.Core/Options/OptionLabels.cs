namespace RoboCopyTo.Core.Options;

/// <summary>
/// Dialog labels. They name the switch each option emits, so they live with the rest of the robocopy
/// knowledge. "_" marks the Alt access key; keys are unique across the options screen
/// (S, D, B, A, V and Y are taken by Start, Dry run, Browse, Save as, Revert and Copy).
/// </summary>
public static class OptionLabels
{
    public const string IncludeSubfolders = "_Include subfolders (/E)";
    public const string Mirror = "_Mirror (/MIR)";
    public const string Restartable = "_Restartable (/Z)";
    public const string Unbuffered = "_Unbuffered I/O (/J)";
    public const string Multithreaded = "Multi_threaded (/MT)";
    public const string RetryCount = "Retry _count (/R)";
    public const string RetryWait = "_Wait seconds (/W)";
    public const string NetworkFriendly = "_Network-friendly";
    public const string CopyPermissions = "Copy _permissions (/COPY:DATS)";
    public const string CopyOwner = "Copy _owner (O)";
    public const string CopyAuditing = "Copy auditin_g info (U)";
    public const string BackupMode = "Bac_kup mode (/B)";
    public const string RestartableBackup = "Restartable with backup _fallback (/ZB)";
    public const string SkipJunctions = "Skip _junctions (/XJ)";
    public const string KeepFolderTimestamps = "K_eep folder timestamps (/DCOPY:DAT)";
    public const string ExcludeFiles = "Exclude fi_les (/XF)";
    public const string ExcludeFolders = "Exclude folders (/XD)";
    public const string ExtraArguments = "E_xtra arguments";
}
