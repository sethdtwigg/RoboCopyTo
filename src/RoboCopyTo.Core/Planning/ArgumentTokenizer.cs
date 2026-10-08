using System.Text;

namespace RoboCopyTo.Core.Planning;

/// <summary>Splits free-text arguments on whitespace, keeping double-quoted runs together.</summary>
public static class ArgumentTokenizer
{
    /// <summary>Tokens with their quotes preserved, so they can be re-joined verbatim.</summary>
    public static IReadOnlyList<string> Split(string? text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return tokens;

        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var c in text)
        {
            if (c == '"')
                inQuotes = !inQuotes;
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0)
            tokens.Add(current.ToString());
        return tokens;
    }

    /// <summary>Tokens with surrounding quotes removed, for pattern lists.</summary>
    public static IReadOnlyList<string> SplitUnquoted(string? text)
        => Split(text).Select(t => t.Replace("\"", "")).Where(t => t.Length > 0).ToList();

    /// <summary>
    /// The canonical form robocopy sees: quotes removed, a leading '-' read as '/', upper case.
    /// Robocopy accepts "/MIR", -MIR and -mir as /MIR, so safety checks must compare this form.
    /// </summary>
    public static string NormalizeSwitch(string token)
    {
        var t = token.Replace("\"", "").Trim();
        if (t.StartsWith('-'))
            t = "/" + t[1..];
        return t.ToUpperInvariant();
    }

    /// <summary>True when <paramref name="token"/> is one of <paramref name="switches"/> (written as "/X") in any form robocopy accepts.</summary>
    public static bool IsSwitch(string token, params string[] switches)
    {
        var n = NormalizeSwitch(token);
        return switches.Any(s => string.Equals(n, s, StringComparison.OrdinalIgnoreCase));
    }

    public static bool ContainsSwitch(string? text, params string[] switches)
        => Split(text).Any(t => IsSwitch(t, switches));
}
