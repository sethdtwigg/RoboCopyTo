using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.Core.Tests.Planning;

public class SelectionAnalyzerTests
{
    private sealed class FakeProbe(params string[] folders) : IPathProbe
    {
        private readonly HashSet<string> _folders = new(folders.Select(PathUtil.Normalize), StringComparer.OrdinalIgnoreCase);
        public bool DirectoryExists(string path) => _folders.Contains(PathUtil.Normalize(path));
        public bool FileExists(string path) => !DirectoryExists(path);
    }

    [Fact]
    public void Removes_duplicates_case_insensitively_and_ignores_trailing_backslash()
    {
        var result = SelectionAnalyzer.Normalize([@"C:\a\x.txt", @"c:\A\X.TXT", @"C:\b\", @"C:\b"]);
        Assert.Equal([@"C:\a\x.txt", @"C:\b"], result);
    }

    [Fact]
    public void Drops_paths_inside_another_selected_folder()
    {
        var result = SelectionAnalyzer.Normalize([@"C:\a\b\c.txt", @"C:\a", @"C:\a\b", @"C:\ab\d.txt"]);
        Assert.Equal([@"C:\a", @"C:\ab\d.txt"], result);
    }

    [Fact]
    public void Ignores_blank_entries()
    {
        Assert.Equal([@"C:\a"], SelectionAnalyzer.Normalize(["", "  ", @"C:\a"]));
    }

    [Fact]
    public void Classifies_files_folders_and_drive_roots()
    {
        var probe = new FakeProbe(@"C:\Photos", @"E:\");
        var analysis = SelectionAnalyzer.Analyze([@"C:\Photos", @"C:\Docs\a.txt", @"E:\"], probe);

        Assert.Equal(
            [new SelectedItem(@"C:\Photos", ItemKind.Folder), new SelectedItem(@"C:\Docs\a.txt", ItemKind.File), new SelectedItem(@"E:\", ItemKind.DriveRoot)],
            analysis.Items);
        Assert.True(analysis.HasLooseFiles);
        Assert.True(analysis.HasDriveRoots);
    }

    [Fact]
    public void Folder_only_selection_has_no_loose_files()
    {
        var analysis = SelectionAnalyzer.Analyze([@"C:\Photos"], new FakeProbe(@"C:\Photos"));
        Assert.False(analysis.HasLooseFiles);
        Assert.False(analysis.HasDriveRoots);
    }
}
