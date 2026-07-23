using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Octadock.App.Windows;

/// <summary>
/// Shared entrance motion for Octadock windows: a short fade (optionally with a
/// slight scale-up) played only when the app-wide <c>Octadock.Motion.Enabled</c>
/// flag allows it. ThemeManager keeps that flag in sync with the Windows
/// animation setting, so reduced-motion users get the final state instantly
/// with no animation at all. Durations come from the Octadock.Motion.Duration.*
/// tokens.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class EntranceMotion
{
    /// <summary>
    /// Plays the entrance fade on <paramref name="root"/> (which must start at
    /// Opacity 0), or snaps it straight to visible when motion is reduced.
    /// When <paramref name="scale"/> is true and the root carries a ScaleTransform,
    /// the transform animates from 0.98 to 1 alongside the fade.
    /// </summary>
    public static void Play(FrameworkElement root, bool scale = false)
    {
        if (!IsEnabled(root))
        {
            root.Opacity = 1;
            if (root.RenderTransform is ScaleTransform settled)
            {
                settled.ScaleX = 1;
                settled.ScaleY = 1;
            }

            return;
        }

        Duration duration = root.TryFindResource("Octadock.Motion.Duration.Fast") is Duration fast
            ? fast
            : new Duration(TimeSpan.FromMilliseconds(120));

        root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0d, 1d, duration));

        if (scale && root.RenderTransform is ScaleTransform transform)
        {
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.98, 1d, duration));
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.98, 1d, duration));
        }
    }

    private static bool IsEnabled(FrameworkElement root)
        => root.TryFindResource("Octadock.Motion.Enabled") is bool enabled
            ? enabled
            : SystemParameters.ClientAreaAnimation;
}
