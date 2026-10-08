using System.Text;

namespace RoboCopyTo.Core.Logging;

/// <summary>Reads UTF-16LE text appended to a log file after a given offset, while robocopy writes it.</summary>
public sealed class LogTailer : IDisposable
{
    private readonly string _path;
    private readonly Decoder _decoder = new UnicodeEncoding(false, false).GetDecoder();
    private readonly byte[] _buffer = new byte[64 * 1024];
    private readonly char[] _chars = new char[64 * 1024];
    private FileStream? _stream;
    private long _position;

    public LogTailer(string path, long startOffset)
    {
        _path = path;
        _position = startOffset;
    }

    public static long CurrentLength(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    /// <summary>Returns any new text, or "" if nothing was appended (or the file is not readable yet).</summary>
    public string ReadNew()
    {
        try
        {
            _stream ??= new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }

        var sb = new StringBuilder();
        _stream.Position = _position;
        int read;
        while ((read = _stream.Read(_buffer, 0, _buffer.Length)) > 0)
        {
            _position += read;
            var n = _decoder.GetChars(_buffer, 0, read, _chars, 0, flush: false);
            sb.Append(_chars, 0, n);
        }
        return sb.Replace("﻿", "").ToString();
    }

    public void Dispose() => _stream?.Dispose();
}
