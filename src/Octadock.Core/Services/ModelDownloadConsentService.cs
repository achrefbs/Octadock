using Octadock.Core.Abstractions;

namespace Octadock.Core.Services;

/// <summary>
/// One-time consent policy for large model downloads (WS7, R6). Consent is
/// persisted in <see cref="Settings.SpeechSettings.ModelDownloadConsented"/>;
/// once granted the prompt is never shown again. Declining leaves the flag
/// untouched, so the next download attempt asks again rather than silently
/// fetching.
/// </summary>
public sealed class ModelDownloadConsentService : IModelDownloadConsent
{
    private readonly ISettingsService _settings;
    private readonly IModelDownloadConsentPrompt _prompt;

    /// <summary>Creates the consent policy.</summary>
    public ModelDownloadConsentService(ISettingsService settings, IModelDownloadConsentPrompt prompt)
    {
        _settings = settings;
        _prompt = prompt;
    }

    /// <inheritdoc />
    public async Task<bool> EnsureConsentAsync(
        ModelDownloadConsentRequest request, CancellationToken cancellationToken = default)
    {
        if (_settings.Current.Speech.ModelDownloadConsented)
        {
            return true;
        }

        bool allowed = await _prompt.RequestAsync(request, cancellationToken).ConfigureAwait(false);
        if (!allowed)
        {
            return false;
        }

        await _settings.UpdateAsync(
            s => s with { Speech = s.Speech with { ModelDownloadConsented = true } },
            cancellationToken).ConfigureAwait(false);
        return true;
    }
}
