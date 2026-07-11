namespace Octadock.WorkflowIntelligence.Internal;

internal static class ProviderRoots
{
    internal static string Claude
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    internal static string Codex
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");

    internal static string For(ProviderKind provider) => provider switch
    {
        ProviderKind.Claude => Claude,
        ProviderKind.Codex => Codex,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };
}
