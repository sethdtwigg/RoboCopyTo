using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RoboCopyTo.Core.Execution;

public sealed record FailedItem(string Path, int ErrorCode, string Action, string Message);

public sealed record SummaryRow(long Total, long Copied, long Skipped, long Mismatch, long Failed, long Extras);

public sealed record RobocopySummary(SummaryRow Dirs, SummaryRow Files, SummaryRow Bytes);

/// <summary>
/// Incremental parser for robocopy output produced with /BYTES /FP /NP. Built from captured
/// output with and without /MT (see the test fixtures).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A file line is "\t  &lt;class&gt;  \t\t &lt;size&gt;\t&lt;full path&gt;". Skipped (same) files are not listed.</item>
/// <item>Directory lines end in a backslash and are ignored. "*EXTRA" lines name destination items and are not progress.</item>
/// <item>"yyyy/MM/dd HH:mm:ss ERROR n (0x..) &lt;action&gt; &lt;path&gt;" is followed by a message line.</item>
/// <item>A file whose last event is an error has failed; if it is listed again with no error, a retry succeeded.
/// (Single-threaded /R:0 does not print "RETRY LIMIT EXCEEDED", so that line cannot be relied on.)</item>
/// <item>With /MT, a "Waiting n seconds..." notice can be glued to the end of an unrelated file line.</item>
/// </list>
/// </remarks>
public sealed partial class OutputParser
{
    private enum FileState { Done, Failed }

    private sealed class Entry
    {
        public long Size;
        public FileState State;
        public FailedItem? Failure;
    }

    private readonly Dictionary<string, Entry> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FailedItem> _otherFailures = [];
    private readonly StringBuilder _partial = new();
    private (int Code, string Action, string Path)? _pendingError;
    private SummaryRow? _dirs, _filesRow, _bytes;

    public long FilesDone { get; private set; }
    public long BytesDone { get; private set; }
    public long ExtrasListed { get; private set; }
    public string? CurrentFile { get; private set; }

    /// <summary>Raised for every complete line, after splitting /MT run-on lines.</summary>
    public event Action<string>? LineParsed;

    public RobocopySummary? Summary => _dirs is not null && _filesRow is not null && _bytes is not null
        ? new RobocopySummary(_dirs, _filesRow, _bytes)
        : null;

    public IReadOnlyList<FailedItem> Failures =>
        _files.Values.Where(e => e.State == FileState.Failed && e.Failure is not null).Select(e => e.Failure!)
            .Concat(_otherFailures)
            .ToList();

    /// <summary>Feeds a chunk of text; incomplete trailing lines are held until more text or <see cref="Complete"/>.</summary>
    public void FeedText(string chunk)
    {
        foreach (var c in chunk)
        {
            if (c == '\n')
            {
                var line = _partial.ToString().TrimEnd('\r');
                _partial.Clear();
                Feed(line);
            }
            else
            {
                _partial.Append(c);
            }
        }
    }

    /// <summary>Flushes any held partial line and any error still waiting for its message.</summary>
    public void Complete()
    {
        if (_partial.Length > 0)
        {
            var line = _partial.ToString().TrimEnd('\r');
            _partial.Clear();
            Feed(line);
        }
        if (_pendingError is { } e)
        {
            RecordError(e.Code, e.Action, e.Path, "");
            _pendingError = null;
        }
    }

    public void Feed(string rawLine)
    {
        var line = rawLine.Replace("\uFEFF", "");
        var glued = GluedNotice().Match(line);
        if (glued.Success && glued.Index > 0)
        {
            FeedOne(line[..glued.Index]);
            FeedOne(glued.Value);
            return;
        }
        FeedOne(line);
    }

    private void FeedOne(string line)
    {
        LineParsed?.Invoke(line);

        if (_pendingError is { } pending)
        {
            if (line.Trim().Length == 0)
                return; // message line not seen yet
            if (!ErrorLine().IsMatch(line) && !FileLine().IsMatch(line))
            {
                RecordError(pending.Code, pending.Action, pending.Path, line.Trim());
                _pendingError = null;
                return;
            }
            RecordError(pending.Code, pending.Action, pending.Path, "");
            _pendingError = null;
        }

        var m = FileLine().Match(line);
        if (m.Success)
        {
            var cls = m.Groups["cls"].Value.Trim();
            var path = m.Groups["path"].Value;
            var size = long.Parse(m.Groups["size"].Value, CultureInfo.InvariantCulture);
            if (path.EndsWith('\\'))
                return; // directory line
            if (cls.StartsWith('*') || cls.StartsWith("EXTRA", StringComparison.OrdinalIgnoreCase))
            {
                ExtrasListed++;
                return;
            }
            OnFile(path, size);
            return;
        }

        var e = ErrorLine().Match(line);
        if (e.Success)
        {
            _pendingError = (int.Parse(e.Groups["code"].Value, CultureInfo.InvariantCulture), e.Groups["action"].Value, e.Groups["path"].Value);
            return;
        }

        var s = SummaryLine().Match(line);
        if (s.Success)
        {
            var row = new SummaryRow(N(s, 1), N(s, 2), N(s, 3), N(s, 4), N(s, 5), N(s, 6));
            switch (s.Groups["label"].Value)
            {
                case "Dirs": _dirs = row; break;
                case "Files": _filesRow = row; break;
                case "Bytes": _bytes = row; break;
            }
        }
    }

    private void OnFile(string path, long size)
    {
        CurrentFile = path;
        if (_files.TryGetValue(path, out var entry))
        {
            if (entry.State == FileState.Failed)
            {
                // Listed again: a retry is in progress; count it unless it errors again.
                entry.State = FileState.Done;
                entry.Size = size;
                FilesDone++;
                BytesDone += size;
            }
            return;
        }
        _files[path] = new Entry { Size = size, State = FileState.Done };
        FilesDone++;
        BytesDone += size;
    }

    private void RecordError(int code, string action, string path, string message)
    {
        var failure = new FailedItem(path, code, action, message);
        if (action.EndsWith("File", StringComparison.OrdinalIgnoreCase) && _files.TryGetValue(path, out var entry))
        {
            if (entry.State == FileState.Done)
            {
                FilesDone--;
                BytesDone -= entry.Size;
            }
            entry.State = FileState.Failed;
            entry.Failure = failure;
            return;
        }
        if (!_otherFailures.Any(f => f.Path == path && f.Action == action && f.ErrorCode == code))
            _otherFailures.Add(failure);
    }

    private static long N(Match m, int i) => long.Parse(m.Groups["n" + i].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\t\s*(?<cls>[^\t]*?)\s*\t\t?\s*(?<size>\d+)\t(?<path>.+)$")]
    private static partial Regex FileLine();

    [GeneratedRegex(@"^\d{4}/\d\d/\d\d \d\d:\d\d:\d\d ERROR (?<code>\d+) \(0x[0-9A-Fa-f]+\) (?<action>.+?) (?<path>(?:[A-Za-z]:\\|\\\\).*)$")]
    private static partial Regex ErrorLine();

    [GeneratedRegex(@"^\s*(?<label>Dirs|Files|Bytes)\s*:\s*(?<n1>\d+)\s+(?<n2>\d+)\s+(?<n3>\d+)\s+(?<n4>\d+)\s+(?<n5>\d+)\s+(?<n6>\d+)\s*$")]
    private static partial Regex SummaryLine();

    [GeneratedRegex(@"Waiting \d+ seconds\.\.\.(?: Retrying\.\.\.)?$")]
    private static partial Regex GluedNotice();
}
