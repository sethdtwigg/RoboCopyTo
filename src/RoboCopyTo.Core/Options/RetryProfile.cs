namespace RoboCopyTo.Core.Options;

/// <summary>A retry count and wait time pair, emitted as /R:n /W:n.</summary>
public readonly record struct RetryProfile(int Count, int WaitSeconds)
{
    public static readonly RetryProfile Default = new(3, 5);

    /// <summary>Used while the Network-friendly toggle is on, together with /TBD.</summary>
    public static readonly RetryProfile NetworkFriendly = new(10, 15);
}
