namespace Octadock.App.Services;

/// <summary>
/// Serializes interactive capture flows process-wide. Any flow that shows a
/// selection overlay and then grabs pixels (area/window/fullscreen capture,
/// OCR region grabs) must hold this gate for the whole selection-plus-grab
/// span, so two flows can never fight over the overlay windows or capture
/// each other's UI (H2). Registered as a singleton and shared by
/// <see cref="CaptureCoordinator"/> and <see cref="OcrService"/>.
/// </summary>
public sealed class CaptureGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Attempts to enter the gate without waiting. Returns <c>false</c> when
    /// another capture flow currently holds it (callers typically notify the
    /// user, then <see cref="EnterAsync"/> to queue).
    /// </summary>
    public Task<bool> TryEnterImmediatelyAsync(CancellationToken cancellationToken)
        => _semaphore.WaitAsync(TimeSpan.Zero, cancellationToken);

    /// <summary>Waits until the gate can be entered.</summary>
    public Task EnterAsync(CancellationToken cancellationToken)
        => _semaphore.WaitAsync(cancellationToken);

    /// <summary>Releases the gate. Call exactly once per successful enter.</summary>
    public void Exit() => _semaphore.Release();

    /// <summary>Disposed by the service provider at shutdown (singleton).</summary>
    public void Dispose() => _semaphore.Dispose();
}
