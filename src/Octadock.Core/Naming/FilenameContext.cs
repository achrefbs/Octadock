using Octadock.Core.Models;

namespace Octadock.Core.Naming;

/// <summary>
/// Inputs available to the filename template engine when expanding tokens such
/// as <c>{process}</c>, <c>{window}</c>, <c>{type}</c> and <c>{counter}</c>.
/// </summary>
public sealed record FilenameContext
{
    public required DateTimeOffset Timestamp { get; init; }

    public CaptureType Type { get; init; } = CaptureType.Area;

    public string? ProcessName { get; init; }

    public string? WindowTitle { get; init; }

    /// <summary>Monotonic counter for disambiguating same-second captures.</summary>
    public int Counter { get; init; }
}
