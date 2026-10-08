using System.Diagnostics;
using System.Globalization;
using System.Text;
using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.Core.Tests.Integration;

/// <summary>Runs a robocopy argument string exactly as given and captures stdout.</summary>
public static class Robo
{
    public sealed record Result(int ExitCode, string Output);

    static Robo() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Result Run(string arguments)
    {
        var psi = new ProcessStartInfo("robocopy.exe", arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(120_000))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException("robocopy did not finish: " + arguments);
        }
        return new Result(p.ExitCode, output);
    }

    public static IReadOnlyList<Result> RunAll(IEnumerable<RobocopyJob> jobs) => jobs.Select(j => Run(j.Arguments)).ToList();

    /// <summary>Reads robocopy's "Source :" / "Dest :" header line.</summary>
    public static string HeaderValue(string output, string label)
    {
        foreach (var line in output.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith(label, StringComparison.Ordinal) && t.Contains(':'))
                return t[(t.IndexOf(':') + 1)..].Trim();
        }
        throw new InvalidOperationException($"No '{label}' line in output:\n{output}");
    }
}
