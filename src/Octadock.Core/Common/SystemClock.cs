namespace Octadock.Core.Common;

/// <summary>Default <see cref="IClock"/> backed by the operating system clock.</summary>
public sealed class SystemClock : IClock
{
    /// <summary>A shared, thread-safe instance.</summary>
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateTimeOffset LocalNow => DateTimeOffset.Now;
}
