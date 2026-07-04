namespace Octadock.Core.Geometry;

/// <summary>
/// Stable identifier for a physical display. Wraps the OS device name (for
/// example <c>\\.\DISPLAY1</c>) so capture metadata can be correlated back to a
/// monitor across sessions even when the monitor index changes.
/// </summary>
public readonly record struct MonitorId
{
    /// <summary>A sentinel value used when the monitor is unknown.</summary>
    public static readonly MonitorId Unknown = new("UNKNOWN");

    public MonitorId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value) ? "UNKNOWN" : value.Trim();
    }

    /// <summary>The underlying device identifier.</summary>
    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(MonitorId id) => id.Value;
}
