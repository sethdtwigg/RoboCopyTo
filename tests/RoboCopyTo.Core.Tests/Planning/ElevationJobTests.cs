using System.Diagnostics;
using RoboCopyTo.Core.Options;
using RoboCopyTo.Core.Planning;
using RoboCopyTo.Core.Shell;
using RoboCopyTo.Core.Tests.Integration;

namespace RoboCopyTo.Core.Tests.Planning;

public sealed class ElevationJobTests : IDisposable
{
    private readonly TempDir _t = new();

    public void Dispose() => _t.Dispose();

    /// <summary>A job folder shaped like %LocalAppData%\RoboCopyTo\Temp, inside the test's temp dir.</summary>
    private string JobDir => _t.Dir(@"Profile\AppData\Local\RoboCopyTo\Temp");

    private static ElevationJob Sample() =>
        new([@"C:\a", @"C:\b.txt"], @"D:\dest", new RobocopyOptions { CopyOwner = true, Threads = 3 }, true, "Mine");

    private static string? Lookup(string drive) => drive switch
    {
        "Z:" => @"\\nas\media",
        "Y:" => @"\\server\share\",
        _ => null,
    };

    [Theory]
    [InlineData(@"Z:\Movies\a.mkv", @"\\nas\media\Movies\a.mkv")]
    [InlineData(@"z:\Movies", @"\\nas\media\Movies")]
    [InlineData(@"Z:\", @"\\nas\media")]
    [InlineData(@"Z:", @"\\nas\media")]
    [InlineData(@"Y:\x", @"\\server\share\x")]
    [InlineData(@"C:\Local", @"C:\Local")]
    [InlineData(@"\\other\share\x", @"\\other\share\x")]
    public void Mapped_drives_convert_to_unc(string input, string expected)
        => Assert.Equal(expected, MappedDrives.ToUnc(input, Lookup));

    [Fact]
    public void Local_fixed_drive_is_not_a_network_connection()
        => Assert.Null(MappedDrives.GetConnection(Path.GetPathRoot(Environment.SystemDirectory)![..2]));

    [Fact]
    public void Job_converts_all_paths()
    {
        var job = new ElevationJob([@"Z:\a", @"C:\b"], @"Y:\dest", new RobocopyOptions(), false, null)
            .WithUncPaths(p => MappedDrives.ToUnc(p, Lookup));
        Assert.Equal([@"\\nas\media\a", @"C:\b"], job.Sources);
        Assert.Equal(@"\\server\share\dest", job.Destination);
    }

    [Fact]
    public void Write_then_read_round_trips_and_deletes_the_file()
    {
        var job = Sample();
        var (path, hash) = job.Write(JobDir);
        Assert.True(File.Exists(path));

        var read = ElevationJob.ReadAndDelete(path, hash);

        Assert.False(File.Exists(path));
        Assert.Equal(job.Sources, read.Sources);
        Assert.Equal(job.Destination, read.Destination);
        Assert.Equal(job.Options, read.Options);
        Assert.True(read.DryRun);
        Assert.Equal("Mine", read.PresetName);
    }

    [Fact]
    public void A_job_file_changed_after_writing_is_rejected_and_deleted()
    {
        var (path, hash) = Sample().Write(JobDir);
        File.WriteAllText(path, File.ReadAllText(path).Replace(@"D:\\dest", @"C:\\Windows"));

        var ex = Assert.Throws<InvalidOperationException>(() => ElevationJob.ReadAndDelete(path, hash));
        Assert.Contains("changed", ex.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_job_in_another_accounts_profile_is_accepted()
    {
        // The elevating admin's own %LocalAppData% differs; the job lives in the original user's profile.
        var otherProfile = _t.Dir(@"Users\standarduser\AppData\Local\RoboCopyTo\Temp");
        Assert.NotEqual(Path.GetFullPath(ElevationJob.DefaultDirectory), otherProfile);
        var (path, hash) = Sample().Write(otherProfile);

        Assert.Equal(@"D:\dest", ElevationJob.ReadAndDelete(path, hash).Destination);
    }

    [Theory]
    [InlineData(@"elsewhere\job-0123456789abcdef0123456789abcdef.json")]
    [InlineData(@"RoboCopyTo\Logs\job-0123456789abcdef0123456789abcdef.json")]
    [InlineData(@"RoboCopyTo\Temp\notes.json")]
    [InlineData(@"RoboCopyTo\Temp\job-0123456789abcdef0123456789abcdef.txt")]
    public void Refuses_files_that_are_not_job_files_in_a_RoboCopyTo_Temp_folder(string relative)
    {
        var file = _t.File(relative, "{}");
        Assert.False(ElevationJob.IsAcceptablePath(file, out _));
        Assert.Throws<InvalidOperationException>(() => ElevationJob.ReadAndDelete(file, "00"));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Refuses_a_temp_folder_that_is_a_junction()
    {
        var real = _t.Dir("real");
        var app = _t.Dir(@"Linked\RoboCopyTo");
        var junction = Path.Combine(app, "Temp");
        var mk = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{real}\"") { CreateNoWindow = true, UseShellExecute = false })!;
        mk.WaitForExit();
        Assert.True(Directory.Exists(junction), "mklink /J should work without elevation");
        var (path, hash) = Sample().Write(junction);

        Assert.False(ElevationJob.IsAcceptablePath(path, out var reason));
        Assert.Contains("junction", reason);
        Assert.Throws<InvalidOperationException>(() => ElevationJob.ReadAndDelete(path, hash));
        Directory.Delete(junction); // removes the link only
    }

    [Fact]
    public void Options_read_from_a_job_are_sanitized()
    {
        var dir = JobDir;
        var path = Path.Combine(dir, "job-" + Guid.NewGuid().ToString("N") + ".json");
        var json = """{ "sources": ["C:\\a"], "destination": "D:\\d", "options": { "threads": 999, "extraArguments": null }, "dryRun": false }""";
        File.WriteAllText(path, json);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

        var job = ElevationJob.ReadAndDelete(path, hash);

        Assert.Equal(128, job.Options.Threads);
        Assert.Equal("", job.Options.ExtraArguments);
    }
}
