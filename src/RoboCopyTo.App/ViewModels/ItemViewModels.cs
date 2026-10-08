using CommunityToolkit.Mvvm.ComponentModel;
using RoboCopyTo.Core;
using RoboCopyTo.Core.Execution;
using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.App.ViewModels;

public sealed class SelectedItemViewModel(SelectedItem item)
{
    public SelectedItem Item { get; } = item;
    public string Path => Item.Path;
    public bool IsFolder => Item.Kind != ItemKind.File;

    /// <summary>Segoe Fluent Icons: Folder / Document / HardDrive.</summary>
    public string Glyph => Item.Kind switch
    {
        ItemKind.Folder => "",
        ItemKind.DriveRoot => "",
        _ => "",
    };

    public string KindText => Item.Kind switch
    {
        ItemKind.Folder => "Folder",
        ItemKind.DriveRoot => "Drive (not supported)",
        _ => "File",
    };
}

/// <summary>One robocopy call in the command preview.</summary>
public sealed class PreviewJobViewModel(RobocopyJob job)
{
    public RobocopyJob Job { get; } = job;
    public IReadOnlyList<ArgumentSegment> Segments { get; } = [new ArgumentSegment(RobocopyJob.Executable), .. job.Segments];
}

/// <summary>One robocopy call on the progress and results screens.</summary>
public sealed partial class RunJobViewModel(int number, RobocopyJob job) : ObservableObject
{
    public int Number { get; } = number;
    public RobocopyJob Job { get; } = job;
    public string CommandLine => Job.CommandLine;
    public string Title => Job.Kind == JobKind.Folder
        ? $"{Job.Source}  →  {Job.Destination}"
        : $"{Job.Files.Count} file{(Job.Files.Count == 1 ? "" : "s")} from {Job.Source}  →  {Job.Destination}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusGlyph))]
    public partial JobStatus Status { get; set; }

    [ObservableProperty]
    public partial string ResultText { get; set; } = "";

    [ObservableProperty]
    public partial string CountsText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsError { get; set; }

    public string StatusText => Status switch
    {
        JobStatus.Pending => "Pending",
        JobStatus.Running => "Running",
        JobStatus.Done => "Done",
        JobStatus.Failed => "Failed",
        _ => "Cancelled",
    };

    /// <summary>Segoe Fluent Icons: Clock / Sync / CheckMark / ErrorBadge / Cancel.</summary>
    public string StatusGlyph => Status switch
    {
        JobStatus.Pending => "",
        JobStatus.Running => "",
        JobStatus.Done => "",
        JobStatus.Failed => "",
        _ => "",
    };

    public void ApplyResult(JobRunResult r)
    {
        ResultText = r.Cancelled ? "Cancelled" : $"{r.Interpretation.Text} (exit code {r.ExitCode})";
        IsError = !r.Interpretation.IsSuccess;
        var t = RunTotals.From(r.Summary);
        CountsText = r.Summary is null ? "" : CountsFor(t, Job.IsDryRun);
    }

    public static string CountsFor(RunTotals t, bool dryRun)
        => $"{(dryRun ? "Would copy" : "Copied")} {t.Copied:N0} · Skipped {t.Skipped:N0} · Extra {t.Extras:N0} · Mismatched {t.Mismatched:N0} · Failed {t.Failed:N0}";
}

public sealed class FailedItemViewModel(FailedItem f)
{
    public string Path => f.Path;
    public string Detail => $"{f.Action}: error {f.ErrorCode}{(f.Message.Length > 0 ? " – " + f.Message : "")}";
}

internal static class Bytes
{
    public static string Of(long b) => Format.Bytes(b);
}
