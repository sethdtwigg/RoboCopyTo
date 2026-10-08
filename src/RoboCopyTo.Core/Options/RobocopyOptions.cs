using System.Text.Json.Serialization;

namespace RoboCopyTo.Core.Options;

/// <summary>
/// Every option the dialog can set. Presets store exactly this object, so it must never
/// hold sources or a destination.
/// </summary>
public sealed record RobocopyOptions
{
    public const int MinThreads = 1;
    public const int MaxThreads = 128;

    // Main options
    public bool IncludeSubfolders { get; init; } = true;   // /E
    public bool Mirror { get; init; }                       // /MIR (implies /E)
    public bool Restartable { get; init; }                  // /Z
    public bool Unbuffered { get; init; }                   // /J
    public bool Multithreaded { get; init; } = true;        // /MT:n
    public int Threads { get; init; } = 8;

    // Existing files
    public ExistingFileMode ExistingFiles { get; init; } = ExistingFileMode.CopyIfDifferent;

    // Retries. While NetworkFriendly is on, RetryProfile.NetworkFriendly is used instead,
    // and these keep the user's own values so turning it off restores them.
    public int RetryCount { get; init; } = RetryProfile.Default.Count;
    public int RetryWaitSeconds { get; init; } = RetryProfile.Default.WaitSeconds;
    public bool NetworkFriendly { get; init; }

    // Advanced
    public bool CopyPermissions { get; init; }              // S in /COPY
    public bool CopyOwner { get; init; }                    // O in /COPY, needs elevation
    public bool CopyAuditing { get; init; }                 // U in /COPY, needs elevation
    public bool BackupMode { get; init; }                   // /B, needs elevation
    public bool RestartableBackup { get; init; }            // /ZB replaces /Z, needs elevation
    public bool SkipJunctions { get; init; }                // /XJ
    public bool KeepFolderTimestamps { get; init; } = true; // /DCOPY:DAT
    public string ExcludeFiles { get; init; } = "";         // /XF
    public string ExcludeFolders { get; init; } = "";       // /XD
    public string ExtraArguments { get; init; } = "";

    /// <summary>The retry values actually emitted.</summary>
    [JsonIgnore]
    public RetryProfile EffectiveRetries => NetworkFriendly ? RetryProfile.NetworkFriendly : new RetryProfile(RetryCount, RetryWaitSeconds);

    /// <summary>Mirror always includes subfolders.</summary>
    [JsonIgnore]
    public bool EffectiveIncludeSubfolders => Mirror || IncludeSubfolders;

    [JsonIgnore]
    public int EffectiveThreads => Math.Clamp(Threads, MinThreads, MaxThreads);

    [JsonIgnore]
    public string CopyFlags => "DAT" + (CopyPermissions ? "S" : "") + (CopyOwner ? "O" : "") + (CopyAuditing ? "U" : "");

    [JsonIgnore]
    public bool RequiresElevation => CopyOwner || CopyAuditing || BackupMode || RestartableBackup;

    /// <summary>/Z or /ZB: an interrupted file can be resumed, so a cancelled copy leaves no half-written file behind.</summary>
    [JsonIgnore]
    public bool IsRestartable => Restartable || RestartableBackup;

    /// <summary>
    /// A copy safe to use after loading from JSON: nulls become empty strings, numbers are clamped,
    /// and unknown enum values fall back to the default.
    /// </summary>
    public RobocopyOptions Sanitized() => this with
    {
        Threads = EffectiveThreads,
        RetryCount = Math.Max(0, RetryCount),
        RetryWaitSeconds = Math.Max(0, RetryWaitSeconds),
        ExistingFiles = Enum.IsDefined(ExistingFiles) ? ExistingFiles : ExistingFileMode.CopyIfDifferent,
        ExcludeFiles = ExcludeFiles ?? "",
        ExcludeFolders = ExcludeFolders ?? "",
        ExtraArguments = ExtraArguments ?? "",
    };
}
