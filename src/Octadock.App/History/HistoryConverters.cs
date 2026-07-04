using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Octadock.App.History;

/// <summary>Maps <c>true</c> to <see cref="Visibility.Collapsed"/> and <c>false</c> to <see cref="Visibility.Visible"/>.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}
