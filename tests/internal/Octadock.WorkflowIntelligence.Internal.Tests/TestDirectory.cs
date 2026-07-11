namespace Octadock.WorkflowIntelligence.Internal.Tests;

internal sealed class TestDirectory : IDisposable
{
    internal TestDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "OctadockWorkflowIntelligenceTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    internal string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
