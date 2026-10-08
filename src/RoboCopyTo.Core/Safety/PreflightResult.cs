namespace RoboCopyTo.Core.Safety;

public enum PreflightSeverity
{
    /// <summary>Prevents running.</summary>
    Block,

    /// <summary>Needs explicit confirmation; something will be deleted.</summary>
    Confirm,

    /// <summary>Needs confirmation; the run may not complete as hoped.</summary>
    Warn,

    /// <summary>The destination folder is missing; offer to create it.</summary>
    OfferCreate,
}

public static class PreflightCodes
{
    public const string NothingSelected = "nothing-selected";
    public const string DriveRootSource = "drive-root-source";
    public const string DestinationInvalid = "destination-invalid";
    public const string DestinationIsSource = "destination-is-source";
    public const string DestinationInsideSource = "destination-inside-source";
    public const string MirrorWithLooseFiles = "mirror-with-loose-files";
    public const string MirrorDeletes = "mirror-deletes";
    public const string MoveDeletesSource = "move-deletes-source";
    public const string LowFreeSpace = "low-free-space";
    public const string DestinationMissing = "destination-missing";
    public const string SourceInsideTarget = "source-inside-target";
    public const string TargetCollision = "target-collision";
    public const string CommandTooLong = "command-too-long";
}

public sealed record PreflightIssue(PreflightSeverity Severity, string Code, string Message, IReadOnlyList<string> Details)
{
    public PreflightIssue(PreflightSeverity severity, string code, string message)
        : this(severity, code, message, []) { }
}

public sealed record PreflightResult(IReadOnlyList<PreflightIssue> Issues)
{
    public bool IsBlocked => Issues.Any(i => i.Severity == PreflightSeverity.Block);

    public bool NeedsConfirmation => Issues.Any(i => i.Severity is PreflightSeverity.Confirm or PreflightSeverity.Warn);

    public bool OfferCreateDestination => Issues.Any(i => i.Severity == PreflightSeverity.OfferCreate);

    public IEnumerable<PreflightIssue> Blockers => Issues.Where(i => i.Severity == PreflightSeverity.Block);

    public IEnumerable<PreflightIssue> Confirmations => Issues.Where(i => i.Severity is PreflightSeverity.Confirm or PreflightSeverity.Warn);
}
