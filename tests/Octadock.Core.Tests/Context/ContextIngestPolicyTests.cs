using FluentAssertions;
using Octadock.Core.Context;
using Xunit;

namespace Octadock.Core.Tests.Context;

/// <summary>Snapshot-vs-reference ownership decision at the ~25 MB threshold (WS10).</summary>
public class ContextIngestPolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1024)]
    [InlineData(25L * 1024 * 1024)] // exactly the threshold snapshots
    public void Small_items_are_snapshotted(long size)
        => ContextIngestPolicy.Decide(size).Should().Be(ContextOwnership.Snapshot);

    [Theory]
    [InlineData(25L * 1024 * 1024 + 1)]
    [InlineData(500L * 1024 * 1024)]
    public void Large_items_are_referenced(long size)
        => ContextIngestPolicy.Decide(size).Should().Be(ContextOwnership.Reference);

    [Fact]
    public void The_threshold_is_configurable()
        => ContextIngestPolicy.Decide(2048, thresholdBytes: 1024).Should().Be(ContextOwnership.Reference);
}
