namespace Octadock.Core.Abstractions;

/// <summary>
/// Describes a pending large model download for which the user must give
/// explicit, one-time consent before any bytes are fetched (WS7, R6).
/// </summary>
/// <param name="ModelName">Human-readable name, e.g. "Parakeet speech model".</param>
/// <param name="TotalBytes">Approximate total download size across all files (0 when unknown).</param>
public readonly record struct ModelDownloadConsentRequest(string ModelName, long TotalBytes)
{
    /// <summary>Human-readable provider identity, e.g. "Parakeet (local engine)"; null when unknown.</summary>
    public string? ProviderName { get; init; }

    /// <summary>Where the model files will be stored on this PC; null when unknown.</summary>
    public string? StorageLocation { get; init; }
}

/// <summary>
/// Gate consulted before Octadock downloads a large model over the network. A
/// <c>false</c> result MUST abort the download — no bytes may be fetched without
/// a <c>true</c> result (WS7, R6). Consent is remembered so the prompt appears
/// at most once.
/// </summary>
public interface IModelDownloadConsent
{
    /// <summary>
    /// Returns <c>true</c> if the model may be downloaded: immediately when
    /// consent was already granted, otherwise after prompting the user and
    /// persisting an accepted decision. A declined prompt returns <c>false</c>
    /// and is not remembered, so a later attempt asks again.
    /// </summary>
    Task<bool> EnsureConsentAsync(
        ModelDownloadConsentRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Presents the sized consent prompt and reports the user's decision. Kept
/// separate from <see cref="IModelDownloadConsent"/> so the consent policy
/// (persistence, one-time semantics) stays UI-free and unit-testable; the UI
/// layer supplies the actual dialog.
/// </summary>
public interface IModelDownloadConsentPrompt
{
    /// <summary>Shows the prompt; returns <c>true</c> if the user allows the download.</summary>
    Task<bool> RequestAsync(
        ModelDownloadConsentRequest request, CancellationToken cancellationToken = default);
}
