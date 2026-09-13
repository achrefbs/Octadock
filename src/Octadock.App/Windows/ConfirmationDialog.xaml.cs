using System.Runtime.Versioning;
using System.Windows;

namespace Octadock.App.Windows;

/// <summary>
/// The one themed destructive-action confirmation shared by Octadock's standard
/// windows. It keeps the semantics the native MessageBox confirmations had: the
/// safe answer is the focused default (Enter/Escape cancel), the dialog is owned
/// so focus returns to where the action started, and the confirm button names the
/// action instead of an ambiguous Yes/OK. Only an explicit confirm returns true.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class ConfirmationDialog : Window
{
    private ConfirmationDialog()
    {
        InitializeComponent();
        Octadock.App.Windows.ScreenFit.Attach(this);
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
    {
        var dialog = new ConfirmationDialog
        {
            Title = title,
            Owner = owner ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive),
        };

        if (dialog.Owner is null)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirmText;
        dialog.CancelButton.Content = cancelText;

        return dialog.ShowDialog() == true;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
