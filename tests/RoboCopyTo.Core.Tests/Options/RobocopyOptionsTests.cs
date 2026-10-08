using RoboCopyTo.Core.Options;
using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.Core.Tests.Options;

public class RobocopyOptionsTests
{
    [Fact]
    public void Defaults_match_spec()
    {
        var o = new RobocopyOptions();
        Assert.True(o.IncludeSubfolders);
        Assert.False(o.Mirror);
        Assert.False(o.Restartable);
        Assert.False(o.Unbuffered);
        Assert.True(o.Multithreaded);
        Assert.Equal(8, o.Threads);
        Assert.Equal(ExistingFileMode.CopyIfDifferent, o.ExistingFiles);
        Assert.Equal(new RetryProfile(3, 5), o.EffectiveRetries);
        Assert.False(o.NetworkFriendly);
        Assert.False(o.SkipJunctions);
        Assert.True(o.KeepFolderTimestamps);
        Assert.Equal("DAT", o.CopyFlags);
        Assert.False(o.RequiresElevation);
    }

    [Theory]
    [InlineData(nameof(RobocopyOptions.CopyOwner))]
    [InlineData(nameof(RobocopyOptions.CopyAuditing))]
    [InlineData(nameof(RobocopyOptions.BackupMode))]
    [InlineData(nameof(RobocopyOptions.RestartableBackup))]
    public void Elevation_options(string property)
    {
        var o = new RobocopyOptions();
        o = property switch
        {
            nameof(RobocopyOptions.CopyOwner) => o with { CopyOwner = true },
            nameof(RobocopyOptions.CopyAuditing) => o with { CopyAuditing = true },
            nameof(RobocopyOptions.BackupMode) => o with { BackupMode = true },
            _ => o with { RestartableBackup = true },
        };
        Assert.True(o.RequiresElevation);
    }

    [Fact]
    public void Permissions_alone_does_not_need_elevation()
        => Assert.False(new RobocopyOptions { CopyPermissions = true, Restartable = true }.RequiresElevation);

    [Fact]
    public void Network_friendly_keeps_user_values_for_restoring()
    {
        var o = new RobocopyOptions { RetryCount = 7, RetryWaitSeconds = 9, NetworkFriendly = true };
        Assert.Equal(RetryProfile.NetworkFriendly, o.EffectiveRetries);
        Assert.Equal(new RetryProfile(7, 9), (o with { NetworkFriendly = false }).EffectiveRetries);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(129, 128)]
    [InlineData(64, 64)]
    public void Threads_are_clamped_to_1_through_128(int threads, int expected)
        => Assert.Equal(expected, new RobocopyOptions { Threads = threads }.EffectiveThreads);

    [Fact]
    public void Value_equality_drives_modified_detection()
    {
        Assert.Equal(new RobocopyOptions(), new RobocopyOptions());
        Assert.NotEqual(new RobocopyOptions(), new RobocopyOptions { ExcludeFiles = "*.tmp" });
    }

    [Theory]
    [InlineData("FAT32", true)]
    [InlineData("exFAT", true)]
    [InlineData("NTFS", false)]
    [InlineData("ReFS", false)]
    [InlineData(null, false)]
    public void Fat_family_detection(string? format, bool expected) => Assert.Equal(expected, VolumeInfo.IsFatFamily(format));
}
