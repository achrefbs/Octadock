namespace Octadock.Core.Commands;

/// <summary>Unit system for coordinates supplied to automation commands.</summary>
public enum CoordinateUnits
{
    /// <summary>Physical pixels (default).</summary>
    Pixels = 0,

    /// <summary>WPF device-independent pixels (96 DPI baseline).</summary>
    Dip = 1,
}
