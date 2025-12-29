namespace JitRealm.Mud;

/// <summary>
/// Represents a scheduled callout (delayed method invocation).
/// </summary>
public sealed class CallOutEntry
{
    private static long _nextId;

    /// <summary>
    /// Unique ID for this callout (for cancellation).
    /// </summary>
    public long Id { get; } = Interlocked.Increment(ref _nextId);

    /// <summary>
    /// The object that will receive the callback.
    /// </summary>
    public required string TargetId { get; init; }

    /// <summary>
    /// Name of the method to invoke (via reflection or interface).
    /// </summary>
    public required string MethodName { get; init; }

    /// <summary>
    /// When this callout should fire.
    /// </summary>
    public required DateTimeOffset FireTime { get; init; }

    /// <summary>
    /// Optional arguments to pass to the method.
    /// </summary>
    public object?[]? Args { get; init; }

    /// <summary>
    /// If non-null, this callout repeats at this interval.
    /// </summary>
    public TimeSpan? RepeatInterval { get; init; }

    /// <summary>
    /// Whether this callout has been cancelled.
    /// </summary>
    public bool IsCancelled { get; set; }
}
