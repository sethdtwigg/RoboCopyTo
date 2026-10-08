using System.Text.Json.Serialization;
using RoboCopyTo.Core.Options;

namespace RoboCopyTo.Core.Presets;

/// <summary>A named set of options. Never stores a destination or sources.</summary>
public sealed record Preset(string Name, RobocopyOptions Options)
{
    [JsonIgnore]
    public bool IsBuiltIn { get; init; }

    public const string QuickCopy = "Quick copy";
    public const string MirrorBackup = "Mirror backup";
    public const string LargeFilesOverNetwork = "Large files over network";

    public static IReadOnlyList<Preset> BuiltIns { get; } =
    [
        // /E /MT:8, defaults otherwise.
        new(QuickCopy, new RobocopyOptions()) { IsBuiltIn = true },
        // /MIR /MT:8.
        new(MirrorBackup, new RobocopyOptions { Mirror = true }) { IsBuiltIn = true },
        // /E /Z /MT:4, Network-friendly on.
        new(LargeFilesOverNetwork, new RobocopyOptions { Restartable = true, Threads = 4, NetworkFriendly = true }) { IsBuiltIn = true },
    ];
}
