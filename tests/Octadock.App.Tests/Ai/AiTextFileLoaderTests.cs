using System.IO;
using FluentAssertions;
using Octadock.App.Ai;
using Octadock.Core.Ai;
using Xunit;

namespace Octadock.App.Tests.Ai;

public sealed class AiTextFileLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"octadock-ai-file-{Guid.NewGuid():N}");

    [Fact]
    public async Task Loads_a_bounded_local_text_file_with_only_its_file_name_as_provenance()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "brief.md");
        await File.WriteAllTextAsync(path, "# Brief");

        AiTextFileInput input = await new AiTextFileLoader().LoadAsync(path);

        input.Text.Should().Be("# Brief");
        input.SourceName.Should().Be("brief.md");
    }

    [Fact]
    public async Task Rejects_binary_or_oversized_files()
    {
        Directory.CreateDirectory(_root);
        string binary = Path.Combine(_root, "binary.dat");
        await File.WriteAllBytesAsync(binary, [0x41, 0x00, 0x42]);
        string oversized = Path.Combine(_root, "large.txt");
        await File.WriteAllTextAsync(oversized, new string('x', AiTextActionLimits.MaxInputCharacters + 1));
        var loader = new AiTextFileLoader();

        Func<Task> loadBinary = () => loader.LoadAsync(binary);
        Func<Task> loadOversized = () => loader.LoadAsync(oversized);

        await loadBinary.Should().ThrowAsync<InvalidOperationException>().WithMessage("*binary*");
        await loadOversized.Should().ThrowAsync<InvalidOperationException>().WithMessage("*120,000*");
    }

    [Fact]
    public async Task Rejects_unc_network_paths_before_opening_them()
    {
        Func<Task> load = () => new AiTextFileLoader().LoadAsync(@"\\server\share\notes.txt");

        await load.Should().ThrowAsync<InvalidOperationException>().WithMessage("*local text files*network paths*");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort test cleanup.
        }
    }
}
