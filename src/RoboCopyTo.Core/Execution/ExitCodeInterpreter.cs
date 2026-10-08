namespace RoboCopyTo.Core.Execution;

public sealed record ExitCodeSummary(int Code, bool IsSuccess, bool IsFatal, string Text);

/// <summary>
/// Translates robocopy's bitmask exit code. Values below 8 are successes and are never reported as errors.
/// </summary>
public static class ExitCodeInterpreter
{
    public const int FilesCopied = 1;
    public const int ExtrasPresent = 2;
    public const int Mismatches = 4;
    public const int Failures = 8;
    public const int Fatal = 16;

    public static ExitCodeSummary Interpret(int code, bool mirror, bool dryRun = false)
    {
        if (code < 0)
            return new(code, false, false, $"Robocopy ended unexpectedly (code {code})");
        if (code == 0)
            return new(code, true, false, "Already up to date");

        var parts = new List<string>();
        if ((code & Fatal) != 0)
            parts.Add("Robocopy could not run; check the log");
        if ((code & FilesCopied) != 0)
            parts.Add(dryRun ? "Files would be copied" : "Files copied");
        if ((code & ExtrasPresent) != 0)
            parts.Add(mirror
                ? dryRun ? "Extra items at the destination would be removed" : "Extra items at the destination were removed"
                : "Destination has extra items (not removed)");
        if ((code & Mismatches) != 0)
            parts.Add("Some items mismatched; check the log");
        if ((code & Failures) != 0)
            parts.Add("Some items failed after retries");

        return new(code, code < Failures, code >= Fatal, string.Join("; ", parts));
    }
}
