using Octadock.Core.Common;

namespace Octadock.Core.Tests.Fakes;

/// <summary>A fixed, settable <see cref="IClock"/> for deterministic time in tests.</summary>
internal sealed class TestClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public DateTimeOffset LocalNow => UtcNow;
}
