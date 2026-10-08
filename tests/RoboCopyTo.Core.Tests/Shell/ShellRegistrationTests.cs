using Microsoft.Win32;
using RoboCopyTo.Core.Shell;

namespace RoboCopyTo.Core.Tests.Shell;

/// <summary>Uses a scratch key under HKCU\Software\RoboCopyTo.Tests instead of the real Classes key.</summary>
public sealed class ShellRegistrationTests : IDisposable
{
    private const string Exe = @"C:\Users\me\AppData\Local\Programs\RoboCopyTo\RoboCopyTo.exe";
    private readonly string _scratchPath = @"Software\RoboCopyTo.Tests\" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _root;
    private readonly ShellRegistration _reg;

    public ShellRegistrationTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_scratchPath, writable: true);
        _reg = new ShellRegistration(_root, "TEST");
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_scratchPath, throwOnMissingSubKey: false);
        using var parent = Registry.CurrentUser.OpenSubKey(@"Software\RoboCopyTo.Tests", writable: true);
        if (parent is { SubKeyCount: 0, ValueCount: 0 })
            Registry.CurrentUser.DeleteSubKey(@"Software\RoboCopyTo.Tests", throwOnMissingSubKey: false);
    }

    [Fact]
    public void Register_creates_exactly_the_two_key_trees_with_specified_values()
    {
        _reg.Register(Exe);

        Assert.Equal(["*", "Directory"], _root.GetSubKeyNames().Order(StringComparer.Ordinal));
        foreach (var path in new[] { @"*\shell\RoboCopyTo", @"Directory\shell\RoboCopyTo" })
        {
            using var key = _root.OpenSubKey(path)!;
            Assert.Equal(["", "Icon", "MultiSelectModel"], key.GetValueNames().Order(StringComparer.Ordinal));
            Assert.Equal("RoboCopyTo...", key.GetValue(""));
            Assert.Equal(Exe, key.GetValue("Icon"));
            Assert.Equal("Player", key.GetValue("MultiSelectModel"));
            Assert.Equal(["command"], key.GetSubKeyNames());

            using var cmd = key.OpenSubKey("command")!;
            Assert.Equal([""], cmd.GetValueNames());
            Assert.Equal($"\"{Exe}\" \"%1\"", cmd.GetValue(""));
            Assert.Empty(cmd.GetSubKeyNames());
        }
        using (var star = _root.OpenSubKey("*")!) Assert.Equal(["shell"], star.GetSubKeyNames());
        using (var starShell = _root.OpenSubKey(@"*\shell")!) Assert.Equal(["RoboCopyTo"], starShell.GetSubKeyNames());
    }

    [Fact]
    public void Register_again_updates_exe_path()
    {
        _reg.Register(Exe);
        _reg.Register(@"D:\New\RoboCopyTo.exe");
        Assert.All(_reg.GetStatus(), s => Assert.Equal(@"D:\New\RoboCopyTo.exe", s.ExePath));
    }

    [Fact]
    public void Unregister_removes_exactly_those_trees_and_leaves_siblings()
    {
        using (var other = _root.CreateSubKey(@"*\shell\SomethingElse")) other.SetValue("", "keep me");
        using (var other = _root.CreateSubKey(@"Directory\shell\Another")) other.SetValue("", "keep me");
        _reg.Register(Exe);

        _reg.Unregister();

        Assert.Null(_root.OpenSubKey(@"*\shell\RoboCopyTo"));
        Assert.Null(_root.OpenSubKey(@"Directory\shell\RoboCopyTo"));
        Assert.NotNull(_root.OpenSubKey(@"*\shell\SomethingElse"));
        Assert.NotNull(_root.OpenSubKey(@"Directory\shell\Another"));
    }

    [Fact]
    public void Unregister_when_not_registered_is_harmless()
    {
        _reg.Unregister();
        Assert.All(_reg.GetStatus(), s => Assert.False(s.Exists));
    }

    [Fact]
    public void Status_reports_existence_and_exe_path()
    {
        Assert.All(_reg.GetStatus(), s => Assert.False(s.Exists));
        _reg.Register(Exe);
        var status = _reg.GetStatus();
        Assert.Equal([@"TEST\*\shell\RoboCopyTo", @"TEST\Directory\shell\RoboCopyTo"], status.Select(s => s.KeyPath));
        Assert.All(status, s =>
        {
            Assert.True(s.Exists);
            Assert.Equal(Exe, s.ExePath);
        });
    }

    [Theory]
    [InlineData("\"C:\\a b\\x.exe\" \"%1\"", "C:\\a b\\x.exe")]
    [InlineData("C:\\x.exe %1", "C:\\x.exe")]
    [InlineData("", null)]
    public void ExtractExePath(string command, string? expected) => Assert.Equal(expected, ShellRegistration.ExtractExePath(command));
}
