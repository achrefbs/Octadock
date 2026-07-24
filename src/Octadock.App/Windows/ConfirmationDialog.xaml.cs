using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MahApps.Metro.IconPacks;

namespace Octadock.App.Windows;

/// <summary>
/// The themed explicit-choice dialog shared by Octadock's standard windows. The
/// safe answer is focused/default (Enter/Escape cancel), the dialog is owned so
/// focus returns to where the action started, and every commit button names the
/// action instead of using ambiguous Yes/No copy.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class ConfirmationDialog : Window
{
    private ConfirmationDialogChoice _choice = ConfirmationDialogChoice.Cancel;

    private ConfirmationDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Asks the user to confirm a destructive action described by
    /// <paramref name="title"/>/<paramref name="message"/>; the confirm button shows
    /// the unambiguous <paramref name="confirmText"/> verb. When
    /// <paramref name="owner"/> is null the currently active Octadock window hosts
    /// the dialog so it stays above topmost tool windows and focus returns to the
    /// surface the action started from.
    /// </summary>
    public static bool Confirm(Window? owner, string title, string message, string confirmText, string cancelText = "Cancel")
        => ShowChoice(
            owner,
            title,
            message,
            primaryText: confirmText,
            secondaryText: null,
            cancelText: cancelText,
            tone: ConfirmationDialogTone.Danger) == ConfirmationDialogChoice.Primary;

    /// <summary>
    /// Asks for a non-destructive explicit action. Cancel remains the focused
    /// default, so Enter and Escape cannot opt into a send, download, or account
    /// change without the user first moving focus to the named primary action.
    /// </summary>
    public static bool Ask(
        Window? owner,
        string title,
        string message,
        string primaryText,
        string cancelText = "Cancel",
        ConfirmationDialogTone tone = ConfirmationDialogTone.Neutral)
        => ShowChoice(
            owner,
            title,
            message,
            primaryText,
            secondaryText: null,
            cancelText: cancelText,
            tone: tone) == ConfirmationDialogChoice.Primary;

    /// <summary>
    /// Presents an explicit primary/destructive-secondary/cancel choice. This is
    /// used by the annotation editor so Save, Discard, and Cancel remain distinct.
    /// </summary>
    public static ConfirmationDialogChoice Choose(
        Window? owner,
        string title,
        string message,
        string primaryText,
        string secondaryText,
        string cancelText = "Cancel",
        ConfirmationDialogTone tone = ConfirmationDialogTone.Warning)
        => ShowChoice(
            owner,
            title,
            message,
            primaryText,
            secondaryText,
            cancelText,
            tone);

    private static ConfirmationDialogChoice ShowChoice(
        Window? owner,
        string title,
        string message,
        string primaryText,
        string? secondaryText,
        string cancelText,
        ConfirmationDialogTone tone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryText);
        ArgumentException.ThrowIfNullOrWhiteSpace(cancelText);

        Application? app = Application.Current;
        if (app?.Dispatcher is null ||
            app.Dispatcher.HasShutdownStarted ||
            app.Dispatcher.HasShutdownFinished)
        {
            // Headless or shutting down: decline rather than accept implicitly.
            return ConfirmationDialogChoice.Cancel;
        }

        if (!app.Dispatcher.CheckAccess())
        {
            return app.Dispatcher.Invoke(
                () => ShowChoice(owner, title, message, primaryText, secondaryText, cancelText, tone));
        }

        var dialog = new ConfirmationDialog
        {
            Title = title,
        };

        dialog.Owner = ResolveOwner(app, owner);
        if (dialog.Owner is null)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Topmost = app.Windows
                .OfType<Window>()
                .Any(window => window.IsVisible && window.Topmost);
        }

        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.PrimaryButton.Content = primaryText;
        dialog.CancelButton.Content = cancelText;
        AutomationProperties.SetName(dialog, title);
        AutomationProperties.SetName(dialog.PrimaryButton, primaryText);
        AutomationProperties.SetName(dialog.CancelButton, cancelText);

        if (!string.IsNullOrWhiteSpace(secondaryText))
        {
            dialog.SecondaryButton.Content = secondaryText;
            dialog.SecondaryButton.Visibility = Visibility.Visible;
            AutomationProperties.SetName(dialog.SecondaryButton, secondaryText);
        }

        dialog.ApplyTone(tone);
        _ = dialog.ShowDialog();
        return dialog._choice;
    }

    private static Window? ResolveOwner(Application app, Window? requestedOwner)
    {
        if (requestedOwner is { IsVisible: true })
        {
            return requestedOwner;
        }

        Window? active = app.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsVisible && window.IsActive);
        if (active is not null)
        {
            return active;
        }

        if (app.MainWindow is { IsVisible: true } main)
        {
            return main;
        }

        return app.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsVisible && window.Topmost);
    }

    private void ApplyTone(ConfirmationDialogTone tone)
    {
        string badgeResource;
        string iconResource;
        PackIconLucideKind icon;
        string primaryStyleResource;

        switch (tone)
        {
            case ConfirmationDialogTone.Danger:
                badgeResource = "Octadock.Brush.DangerSoft";
                iconResource = "Octadock.Brush.Danger";
                icon = PackIconLucideKind.TriangleAlert;
                primaryStyleResource = "Octadock.Style.DangerButton.Filled";
                break;
            case ConfirmationDialogTone.Warning:
                badgeResource = "Octadock.Brush.WarningSoft";
                iconResource = "Octadock.Brush.Warning";
                icon = PackIconLucideKind.TriangleAlert;
                primaryStyleResource = "Octadock.Style.AccentButton";
                break;
            default:
                badgeResource = "Octadock.Brush.AccentPill";
                iconResource = "Octadock.Brush.Accent";
                icon = PackIconLucideKind.CircleQuestionMark;
                primaryStyleResource = "Octadock.Style.AccentButton";
                break;
        }

        ToneBadge.SetResourceReference(Border.BackgroundProperty, badgeResource);
        ToneIcon.SetResourceReference(Control.ForegroundProperty, iconResource);
        ToneIcon.Kind = icon;
        PrimaryButton.SetResourceReference(StyleProperty, primaryStyleResource);
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        _choice = ConfirmationDialogChoice.Primary;
        DialogResult = true;
    }

    private void OnSecondaryClick(object sender, RoutedEventArgs e)
    {
        _choice = ConfirmationDialogChoice.Secondary;
        DialogResult = true;
    }
}

/// <summary>Visual emphasis for an explicit-choice dialog.</summary>
public enum ConfirmationDialogTone
{
    Neutral,
    Warning,
    Danger,
}

/// <summary>The only three outcomes produced by <see cref="ConfirmationDialog"/>.</summary>
public enum ConfirmationDialogChoice
{
    Cancel,
    Primary,
    Secondary,
}
