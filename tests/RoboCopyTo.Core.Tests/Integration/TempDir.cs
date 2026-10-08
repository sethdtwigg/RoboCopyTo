namespace RoboCopyTo.Core.Tests.Integration;

/// <summary>A unique folder under %TEMP% that is deleted when the test ends. All robocopy tests stay inside it.</summary>
public sealed class TempDir : IDisposable
{
    public string Root { get; }

    public TempDir()
    {
        Root = Path.Combine(Path.GetTempPath(), "RoboCopyTo.Tests", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Root);
    }

    public string this[string relative] => Path.Combine(Root, relative);

    public string Dir(string relative)
    {
        var p = this[relative];
        Directory.CreateDirectory(p);
        return p;
    }

    public string File(string relative, string content = "content", DateTime? lastWrite = null)
    {
        var p = this[relative];
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, content);
        if (lastWrite is { } t)
            System.IO.File.SetLastWriteTime(p, t);
        return p;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Clear(new DirectoryInfo(Root));
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A robocopy process killed by a test may still be releasing handles; leftovers sit in %TEMP%.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Removes junctions and links without following them (tests create some, including loops), and clears
    /// read-only attributes so the recursive delete can finish.
    /// </summary>
    private static void Clear(DirectoryInfo dir)
    {
        foreach (var entry in dir.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }))
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                if (entry is DirectoryInfo link)
                    link.Delete(); // non-recursive: removes the link only
                else
                    entry.Delete();
            }
            else if (entry is DirectoryInfo sub)
            {
                Clear(sub);
            }
            else
            {
                entry.Attributes = FileAttributes.Normal;
            }
        }
    }
}
