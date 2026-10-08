namespace RoboCopyTo.Core.Planning;

/// <summary>File-system lookups, abstracted so planning and safety logic stay unit-testable.</summary>
public interface IPathProbe
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
}

public sealed class DiskPathProbe : IPathProbe
{
    public static readonly DiskPathProbe Instance = new();
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
}
