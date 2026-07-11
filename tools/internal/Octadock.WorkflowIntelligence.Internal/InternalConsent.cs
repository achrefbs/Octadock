namespace Octadock.WorkflowIntelligence.Internal;

internal enum InternalConsentLevel
{
    Off = 0,
    OctadockActivityOnly,
    WorkflowMetadata,
    FullDeveloperTrace,
}

internal static class InternalConsent
{
    internal const string FullConsentEnvironmentVariable = "OCTADOCK_INTERNAL_FULL_CONSENT";
    internal const string FullConsentValue = "I_UNDERSTAND_RAW_CONTENT_IS_CAPTURED";

    internal static void DemandFullDeveloperTrace()
    {
        string? value = Environment.GetEnvironmentVariable(FullConsentEnvironmentVariable);
        if (!string.Equals(value, FullConsentValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Full developer trace is disabled. Set {FullConsentEnvironmentVariable}=" +
                $"{FullConsentValue} in the internal harness process only after reviewing the consent boundary.");
        }
    }
}
