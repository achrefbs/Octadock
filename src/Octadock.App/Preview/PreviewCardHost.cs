using System.Runtime.Versioning;
using Octadock.App.Theming;
using Octadock.Core.Abstractions;

namespace Octadock.App.Preview;

/// <summary>Testable presentation seam for the single reusable WPF preview card.</summary>
public interface IPreviewCardHost
{
    event EventHandler? Closed;

    void Present(FilePreviewResult result, PreviewCardActions actions);

    void Dismiss();
}

[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class PreviewCardHost : IPreviewCardHost, IDisposable
{
    private readonly ThemeManager _themeManager;
    private PreviewCardWindow? _card;

    public PreviewCardHost(ThemeManager themeManager)
    {
        _themeManager = themeManager ?? throw new ArgumentNullException(nameof(themeManager));
        _themeManager.ThemeApplied += OnThemeApplied;
    }

    public event EventHandler? Closed;

    public void Present(FilePreviewResult result, PreviewCardActions actions)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(actions);

        if (_card is null || _card.IsClosing)
        {
            var card = new PreviewCardWindow(actions);
            card.Closed += OnCardClosed;
            _card = card;
        }

        _card.Present(result);
    }

    public void Dismiss() => _card?.Dismiss();

    private void OnThemeApplied(object? sender, EventArgs e)
    {
        if (_card is { IsClosing: false } card)
        {
            card.RefreshTheme();
        }
    }

    private void OnCardClosed(object? sender, EventArgs e)
    {
        bool closedCurrentCard = false;
        if (sender is PreviewCardWindow card)
        {
            card.Closed -= OnCardClosed;
            if (ReferenceEquals(_card, card))
            {
                _card = null;
                closedCurrentCard = true;
            }
        }

        if (closedCurrentCard)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _themeManager.ThemeApplied -= OnThemeApplied;
        PreviewCardWindow? card = _card;
        _card = null;
        if (card is not null)
        {
            card.Closed -= OnCardClosed;
            if (!card.IsClosing)
            {
                if (card.Dispatcher.CheckAccess())
                {
                    card.Close();
                }
                else
                {
                    card.Dispatcher.Invoke(card.Close);
                }
            }
        }
    }
}
