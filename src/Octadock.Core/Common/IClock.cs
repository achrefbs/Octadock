namespace Octadock.Core.Common;

/// <summary>
/// Abstraction over the system clock so time-dependent logic (retention,
/// filename timestamps, auto-close timers) is deterministic under test.
/// </summary>
public interface IClock
{
    /// <summary>Current UTC time.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Current local time.</summary>
    DateTimeOffset LocalNow { get; }
}
