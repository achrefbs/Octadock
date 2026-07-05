using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.Core.Abstractions;

namespace Octadock.Core.Services;

/// <summary>
/// Default <see cref="IAiSessionChangeBus"/>: a thread-safe multicast where one
/// throwing subscriber can never break the others or the publisher.
/// </summary>
public sealed partial class AiSessionChangeBus : IAiSessionChangeBus
{
    private readonly ILogger<AiSessionChangeBus> _logger;

    /// <summary>Creates the bus.</summary>
    public AiSessionChangeBus(ILogger<AiSessionChangeBus>? logger = null)
    {
        _logger = logger ?? NullLogger<AiSessionChangeBus>.Instance;
    }

    /// <inheritdoc />
    public event EventHandler? SessionsChanged;

    /// <inheritdoc />
    public void Publish()
    {
        EventHandler? handlers = SessionsChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler)handler)(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogSubscriberThrew(ex);
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "An AI-session change subscriber threw.")]
    private partial void LogSubscriberThrew(Exception exception);
}
