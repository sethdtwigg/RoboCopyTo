using RoboCopyTo.Core.Execution;

namespace RoboCopyTo.Core.Tests.Execution;

public class ExitCodeInterpreterTests
{
    [Theory]
    [InlineData(0, "Already up to date")]
    [InlineData(1, "Files copied")]
    [InlineData(2, "Destination has extra items (not removed)")]
    [InlineData(3, "Files copied; Destination has extra items (not removed)")]
    [InlineData(4, "Some items mismatched; check the log")]
    [InlineData(5, "Files copied; Some items mismatched; check the log")]
    [InlineData(6, "Destination has extra items (not removed); Some items mismatched; check the log")]
    [InlineData(7, "Files copied; Destination has extra items (not removed); Some items mismatched; check the log")]
    [InlineData(8, "Some items failed after retries")]
    [InlineData(9, "Files copied; Some items failed after retries")]
    [InlineData(10, "Destination has extra items (not removed); Some items failed after retries")]
    [InlineData(11, "Files copied; Destination has extra items (not removed); Some items failed after retries")]
    [InlineData(12, "Some items mismatched; check the log; Some items failed after retries")]
    [InlineData(13, "Files copied; Some items mismatched; check the log; Some items failed after retries")]
    [InlineData(14, "Destination has extra items (not removed); Some items mismatched; check the log; Some items failed after retries")]
    [InlineData(15, "Files copied; Destination has extra items (not removed); Some items mismatched; check the log; Some items failed after retries")]
    [InlineData(16, "Robocopy could not run; check the log")]
    public void Every_code_maps_to_summary(int code, string expected)
    {
        var s = ExitCodeInterpreter.Interpret(code, mirror: false);
        Assert.Equal(expected, s.Text);
        Assert.Equal(code < 8, s.IsSuccess);
        Assert.Equal(code >= 16, s.IsFatal);
    }

    [Theory]
    [InlineData(2, "Extra items at the destination were removed")]
    [InlineData(3, "Files copied; Extra items at the destination were removed")]
    [InlineData(10, "Extra items at the destination were removed; Some items failed after retries")]
    public void Mirror_reports_extras_removed(int code, string expected)
        => Assert.Equal(expected, ExitCodeInterpreter.Interpret(code, mirror: true).Text);

    [Theory]
    [InlineData(1, false, "Files would be copied")]
    [InlineData(3, true, "Files would be copied; Extra items at the destination would be removed")]
    [InlineData(2, false, "Destination has extra items (not removed)")]
    [InlineData(0, false, "Already up to date")]
    public void Dry_run_uses_conditional_wording(int code, bool mirror, string expected)
        => Assert.Equal(expected, ExitCodeInterpreter.Interpret(code, mirror, dryRun: true).Text);

    [Fact]
    public void Codes_below_eight_are_never_errors()
    {
        for (var code = 0; code < 8; code++)
            Assert.True(ExitCodeInterpreter.Interpret(code, false).IsSuccess);
    }

    [Fact]
    public void Fatal_combined_with_other_bits_lists_fatal_first()
    {
        Assert.Equal("Robocopy could not run; check the log; Files copied", ExitCodeInterpreter.Interpret(17, false).Text);
    }

    [Fact]
    public void Negative_codes_are_failures()
    {
        var s = ExitCodeInterpreter.Interpret(-1, false);
        Assert.False(s.IsSuccess);
        Assert.Equal("Robocopy ended unexpectedly (code -1)", s.Text);
    }
}
