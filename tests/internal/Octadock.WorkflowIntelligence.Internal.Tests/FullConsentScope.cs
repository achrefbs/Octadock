namespace Octadock.WorkflowIntelligence.Internal.Tests;

internal sealed class FullConsentScope : IDisposable
{
    private readonly string? _previous;

    internal FullConsentScope()
    {
        _previous = Environment.GetEnvironmentVariable(InternalConsent.FullConsentEnvironmentVariable);
        Environment.SetEnvironmentVariable(
            InternalConsent.FullConsentEnvironmentVariable,
            InternalConsent.FullConsentValue);
    }

    public void Dispose()
        => Environment.SetEnvironmentVariable(InternalConsent.FullConsentEnvironmentVariable, _previous);
}
