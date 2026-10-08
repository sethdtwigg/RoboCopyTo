using RoboCopyTo.Core.Options;
using RoboCopyTo.Core.Planning;
using RoboCopyTo.Core.Safety;

namespace RoboCopyTo.Core.Tests.Safety;

public class PreflightCheckerTests
{
    private sealed class FakeProbe(string[]? folders = null, string[]? files = null) : IPathProbe
    {
        private readonly HashSet<string> _folders = new((folders ?? []).Select(PathUtil.Normalize), StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _files = new((files ?? []).Select(PathUtil.Normalize), StringComparer.OrdinalIgnoreCase);
        public bool DirectoryExists(string path) => _folders.Contains(PathUtil.Normalize(path));
        public bool FileExists(string path) => _files.Contains(PathUtil.Normalize(path));
    }

    private static SelectedItem Folder(string p) => new(p, ItemKind.Folder);
    private static SelectedItem File(string p) => new(p, ItemKind.File);

    private static PreflightResult Check(
        SelectedItem[] items,
        string dest = @"D:\Backup",
        RobocopyOptions? options = null,
        long? scanned = null,
        long? free = null,
        IPathProbe? probe = null)
    {
        options ??= new RobocopyOptions();
        probe ??= new FakeProbe([@"D:\Backup", @"D:\"]);
        return PreflightChecker.Check(new PreflightInput(items, dest, options, probe, scanned, free));
    }

    private static void AssertHas(PreflightResult r, string code, PreflightSeverity severity)
    {
        var issue = Assert.Single(r.Issues, i => i.Code == code);
        Assert.Equal(severity, issue.Severity);
    }

    private static void AssertLacks(PreflightResult r, string code) => Assert.DoesNotContain(r.Issues, i => i.Code == code);

    [Fact]
    public void Clean_selection_has_no_issues()
    {
        var r = Check([Folder(@"C:\A"), File(@"C:\Docs\a.txt")], scanned: 10, free: 100);
        Assert.Empty(r.Issues);
        Assert.False(r.IsBlocked);
    }

    // Destination inside a selected source folder
    [Fact]
    public void Destination_inside_source_folder_blocks()
    {
        var r = Check([Folder(@"C:\A")], dest: @"C:\A\backup", probe: new FakeProbe([@"C:\A\backup"]));
        AssertHas(r, PreflightCodes.DestinationInsideSource, PreflightSeverity.Block);
        Assert.True(r.IsBlocked);
    }

    [Fact]
    public void Destination_beside_source_folder_passes()
    {
        var r = Check([Folder(@"C:\A")], dest: @"C:\AB", probe: new FakeProbe([@"C:\AB"]));
        AssertLacks(r, PreflightCodes.DestinationInsideSource);
        Assert.False(r.IsBlocked);
    }

    // Destination equals a source
    [Theory]
    [InlineData(@"C:\A")]
    [InlineData(@"c:\a\")]
    public void Destination_equal_to_source_folder_blocks(string dest)
    {
        var r = Check([Folder(@"C:\A")], dest: dest, probe: new FakeProbe([@"C:\A"]));
        AssertHas(r, PreflightCodes.DestinationIsSource, PreflightSeverity.Block);
    }

    [Fact]
    public void Destination_equal_to_loose_file_parent_blocks()
    {
        var r = Check([File(@"C:\Docs\a.txt")], dest: @"C:\Docs", probe: new FakeProbe([@"C:\Docs"]));
        AssertHas(r, PreflightCodes.DestinationIsSource, PreflightSeverity.Block);
    }

    [Fact]
    public void Destination_that_is_parent_of_source_folder_blocks_because_target_is_source()
    {
        // C:\Data\A copied to C:\Data lands at C:\Data\A, which is the source itself.
        var r = Check([Folder(@"C:\Data\A")], dest: @"C:\Data", probe: new FakeProbe([@"C:\Data"]));
        AssertHas(r, PreflightCodes.DestinationIsSource, PreflightSeverity.Block);
    }

    [Fact]
    public void Destination_different_from_sources_passes_equality_check()
    {
        var r = Check([Folder(@"C:\A"), File(@"C:\Docs\a.txt")]);
        AssertLacks(r, PreflightCodes.DestinationIsSource);
    }

    // Mirror or purge with loose files
    [Fact]
    public void Mirror_with_loose_files_blocks()
    {
        var r = Check([Folder(@"C:\A"), File(@"C:\Docs\a.txt")], options: new RobocopyOptions { Mirror = true });
        AssertHas(r, PreflightCodes.MirrorWithLooseFiles, PreflightSeverity.Block);
    }

    [Theory]
    [InlineData("/PURGE")]
    [InlineData("/mir")]
    [InlineData("/NDL /Purge")]
    public void Purge_or_mirror_in_extra_arguments_with_loose_files_blocks(string extra)
    {
        var r = Check([File(@"C:\Docs\a.txt")], options: new RobocopyOptions { ExtraArguments = extra });
        AssertHas(r, PreflightCodes.MirrorWithLooseFiles, PreflightSeverity.Block);
    }

    [Fact]
    public void Mirror_with_only_folders_does_not_block()
    {
        var r = Check([Folder(@"C:\A")], options: new RobocopyOptions { Mirror = true });
        AssertLacks(r, PreflightCodes.MirrorWithLooseFiles);
        Assert.False(r.IsBlocked);
    }

    [Fact]
    public void Similar_switches_in_extra_arguments_are_not_mistaken_for_purge()
    {
        var r = Check([File(@"C:\Docs\a.txt")], options: new RobocopyOptions { ExtraArguments = "/MIRROR_NOT /XF purge.txt" });
        AssertLacks(r, PreflightCodes.MirrorWithLooseFiles);
        AssertLacks(r, PreflightCodes.MirrorDeletes);
    }

    // Destination empty or invalid
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"relative\path")]
    [InlineData(@"C:\bad|name")]
    [InlineData(@"C:\bad<name>")]
    [InlineData(@"C:\a""b")]
    [InlineData(@"\\server")]
    public void Empty_or_invalid_destination_blocks(string dest)
    {
        var r = Check([Folder(@"C:\A")], dest: dest);
        AssertHas(r, PreflightCodes.DestinationInvalid, PreflightSeverity.Block);
    }

    [Fact]
    public void Destination_that_is_an_existing_file_blocks()
    {
        var r = Check([Folder(@"C:\A")], dest: @"D:\file.txt", probe: new FakeProbe(files: [@"D:\file.txt"]));
        AssertHas(r, PreflightCodes.DestinationInvalid, PreflightSeverity.Block);
    }

    [Theory]
    [InlineData(@"D:\Backup")]
    [InlineData(@"E:\")]
    [InlineData(@"\\server\share\dir")]
    public void Valid_destination_passes(string dest)
    {
        var r = Check([Folder(@"C:\A")], dest: dest, probe: new FakeProbe([dest]));
        AssertLacks(r, PreflightCodes.DestinationInvalid);
    }

    // Mirror or purge at all
    [Fact]
    public void Mirror_requires_confirmation_listing_affected_folders()
    {
        var r = Check([Folder(@"C:\A"), Folder(@"C:\B")], options: new RobocopyOptions { Mirror = true });
        var issue = Assert.Single(r.Issues, i => i.Code == PreflightCodes.MirrorDeletes);
        Assert.Equal(PreflightSeverity.Confirm, issue.Severity);
        Assert.Equal([@"D:\Backup\A", @"D:\Backup\B"], issue.Details);
        Assert.True(r.NeedsConfirmation);
        Assert.False(r.IsBlocked);
    }

    [Fact]
    public void Purge_in_extra_arguments_requires_confirmation()
    {
        var r = Check([Folder(@"C:\A")], options: new RobocopyOptions { ExtraArguments = "/PURGE" });
        AssertHas(r, PreflightCodes.MirrorDeletes, PreflightSeverity.Confirm);
    }

    [Fact]
    public void No_mirror_means_no_deletion_confirmation()
    {
        var r = Check([Folder(@"C:\A")]);
        AssertLacks(r, PreflightCodes.MirrorDeletes);
        Assert.False(r.NeedsConfirmation);
    }

    // Move
    [Theory]
    [InlineData("/MOV")]
    [InlineData("/move")]
    public void Move_in_extra_arguments_requires_confirmation(string extra)
    {
        var r = Check([Folder(@"C:\A")], options: new RobocopyOptions { ExtraArguments = extra });
        AssertHas(r, PreflightCodes.MoveDeletesSource, PreflightSeverity.Confirm);
    }

    [Fact]
    public void No_move_means_no_move_confirmation()
    {
        var r = Check([Folder(@"C:\A")], options: new RobocopyOptions { ExtraArguments = "/MOVEMENT" });
        AssertLacks(r, PreflightCodes.MoveDeletesSource);
    }

    // Free space
    [Fact]
    public void Low_free_space_warns()
    {
        var r = Check([Folder(@"C:\A")], scanned: 1000, free: 999);
        AssertHas(r, PreflightCodes.LowFreeSpace, PreflightSeverity.Warn);
        Assert.True(r.NeedsConfirmation);
        Assert.False(r.IsBlocked);
    }

    [Theory]
    [InlineData(1000L, 1000L)]
    [InlineData(1000L, null)]
    [InlineData(null, 5L)]
    public void Enough_or_unknown_free_space_passes(long? scanned, long? free)
    {
        var r = Check([Folder(@"C:\A")], scanned: scanned, free: free);
        AssertLacks(r, PreflightCodes.LowFreeSpace);
    }

    // Destination missing
    [Fact]
    public void Missing_destination_offers_to_create()
    {
        var r = Check([Folder(@"C:\A")], dest: @"D:\New", probe: new FakeProbe([@"D:\"]));
        AssertHas(r, PreflightCodes.DestinationMissing, PreflightSeverity.OfferCreate);
        Assert.True(r.OfferCreateDestination);
        Assert.False(r.IsBlocked);
    }

    [Fact]
    public void Existing_destination_does_not_offer_to_create()
    {
        var r = Check([Folder(@"C:\A")]);
        AssertLacks(r, PreflightCodes.DestinationMissing);
        Assert.False(r.OfferCreateDestination);
    }

    // Drive root source
    [Fact]
    public void Drive_root_source_blocks()
    {
        var r = Check([new SelectedItem(@"E:\", ItemKind.DriveRoot)]);
        AssertHas(r, PreflightCodes.DriveRootSource, PreflightSeverity.Block);
    }

    [Fact]
    public void Empty_selection_blocks()
    {
        var r = Check([]);
        AssertHas(r, PreflightCodes.NothingSelected, PreflightSeverity.Block);
    }
}
