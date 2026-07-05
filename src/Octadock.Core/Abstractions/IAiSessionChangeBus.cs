namespace Octadock.Core.Abstractions;

/// <summary>
/// In-process change notifications for the AI-session store. Writers publish
/// after every mutation (add/update/event) so the overlay, windows, and the
/// process-exit watcher react immediately instead of polling on timers. This
/// is what makes tracked sessions feel real-time.
/// </summary>
public interface IAiSessionChangeBus
{
    /// <summary>Raised (on an arbitrary thread) after any session mutation.</summary>
    event EventHandler? SessionsChanged;

    /// <summary>Notifies all subscribers that session data changed.</summary>
    void Publish();
}
