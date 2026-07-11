namespace Octadock.WorkflowIntelligence.Internal;

internal sealed record TracePaths
{
    internal TracePaths(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Trace root must not be empty.", nameof(root));
        }

        Root = Path.GetFullPath(root);
        string? driveRoot = Path.GetPathRoot(Root);
        if (string.Equals(
                Path.TrimEndingDirectorySeparator(Root),
                Path.TrimEndingDirectorySeparator(driveRoot ?? string.Empty),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Trace root must not be a drive root.", nameof(root));
        }
    }

    internal string Root { get; }

    internal string DatabasePath => Path.Combine(Root, "workflow-traces.internal.db");

    internal string KeyPath => Path.Combine(Root, "workflow-traces.internal.key");

    internal string TempPath => Path.Combine(Root, "temp");

    internal string ExportPath => Path.Combine(Root, "exports");

    internal string CursorsPath => Path.Combine(Root, "cursors");

    internal string SampleManifestPath => Path.Combine(Root, "corpus-sample.internal.json");

    internal static TracePaths CreateDefault()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new TracePaths(Path.Combine(localAppData, "Octadock.Internal", "WorkflowIntelligence"));
    }
}
