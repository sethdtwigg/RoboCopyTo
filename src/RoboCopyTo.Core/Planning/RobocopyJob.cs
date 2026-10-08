namespace RoboCopyTo.Core.Planning;

public enum JobKind
{
    /// <summary>One selected folder copied to &lt;dest&gt;\&lt;folder name&gt;.</summary>
    Folder,

    /// <summary>Loose files from one parent folder copied into &lt;dest&gt;.</summary>
    Files,
}

/// <summary>A piece of the argument string. App-required switches are dimmed in the preview.</summary>
public sealed record ArgumentSegment(string Text, bool IsAppRequired = false);

/// <summary>
/// One robocopy call. <see cref="Arguments"/> is the exact string the preview shows,
/// the runner passes to robocopy, and the log header records.
/// </summary>
public sealed record RobocopyJob(
    JobKind Kind,
    string Source,
    string Destination,
    IReadOnlyList<string> Files,
    IReadOnlyList<ArgumentSegment> Segments,
    bool IsMirror,
    bool DeletesExtras,
    bool IsDryRun,
    string LogPath)
{
    public const string Executable = "robocopy";

    public string Arguments { get; } = string.Join(" ", Segments.Select(s => s.Text));

    public string CommandLine => Executable + " " + Arguments;
}
