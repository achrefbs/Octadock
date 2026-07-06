using FluentAssertions;
using Octadock.Core.Abstractions;
using Octadock.Core.Services;
using Xunit;

namespace Octadock.Core.Tests.Services;

public sealed class PreviewProviderTests : IDisposable
{
    private readonly string _dir;

    public PreviewProviderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "octadock-preview-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string WriteFile(string name, string content)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ---- JSON ------------------------------------------------------------

    [Fact]
    public async Task Json_provider_pretty_prints_valid_json()
    {
        var provider = new JsonPreviewProvider();
        string path = WriteFile("data.json", "{\"name\":\"octadock\",\"tags\":[1,2]}");

        FilePreviewResult result = await provider.LoadAsync(path, new FilePreviewOptions(), CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.PlainText);
        result.Text.Should().Contain("\"name\": \"octadock\"");
        result.Text.Should().Contain("\n");
    }

    [Fact]
    public async Task Json_provider_accepts_comments_and_trailing_commas()
    {
        var provider = new JsonPreviewProvider();
        string path = WriteFile("config.jsonc", "{\n// comment\n\"a\": 1,\n}");

        FilePreviewResult result = await provider.LoadAsync(path, new FilePreviewOptions(), CancellationToken.None);

        result.Text.Should().Contain("\"a\": 1");
    }

    [Fact]
    public async Task Json_provider_shows_invalid_json_raw_with_a_note()
    {
        var provider = new JsonPreviewProvider();
        string path = WriteFile("broken.json", "{ nope }");

        FilePreviewResult result = await provider.LoadAsync(path, new FilePreviewOptions(), CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.PlainText);
        result.Text.Should().StartWith("{ nope }");
        result.Text.Should().Contain("not valid JSON");
    }

    [Fact]
    public void Json_provider_claims_json_extensions_with_priority()
    {
        var provider = new JsonPreviewProvider();

        provider.CanPreview(".json").Should().BeTrue();
        provider.CanPreview(".jsonc").Should().BeTrue();
        provider.CanPreview(".txt").Should().BeFalse();
        ((IFilePreviewProvider)provider).Priority.Should().BeGreaterThan(0);
    }

    // ---- Log ---------------------------------------------------------------

    [Fact]
    public async Task Log_provider_returns_small_files_whole()
    {
        var provider = new LogPreviewProvider();
        string path = WriteFile("app.log", "line1\nline2\nline3");

        FilePreviewResult result = await provider.LoadAsync(path, new FilePreviewOptions(), CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.PlainText);
        result.Text.Should().Be("line1\nline2\nline3");
    }

    [Fact]
    public async Task Log_provider_tails_large_files_and_says_so()
    {
        var provider = new LogPreviewProvider();
        var content = new System.Text.StringBuilder();
        for (int i = 0; i < 40_000; i++)
        {
            content.Append("log line number ").Append(i).Append('\n');
        }

        string path = WriteFile("big.log", content.ToString());

        FilePreviewResult result = await provider.LoadAsync(path, new FilePreviewOptions(), CancellationToken.None);

        result.Text.Should().StartWith("… (showing the last 256 KB");
        result.Text.Should().Contain("log line number 39999");
        result.Text.Should().NotContain("log line number 0\n");
    }

    // ---- Markdown -----------------------------------------------------------

    [Fact]
    public async Task Markdown_provider_returns_markdown_kind()
    {
        var provider = new MarkdownPreviewProvider();
        string path = WriteFile("README.md", "# Title\n\nSome *text*.");

        FilePreviewResult result = await provider.LoadAsync(path, new FilePreviewOptions(), CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.Markdown);
        result.Text.Should().Be("# Title\n\nSome *text*.");
    }

    [Fact]
    public void Markdown_provider_claims_md_extensions()
    {
        var provider = new MarkdownPreviewProvider();

        provider.CanPreview(".md").Should().BeTrue();
        provider.CanPreview(".markdown").Should().BeTrue();
        provider.CanPreview(".txt").Should().BeFalse();
    }

    // ---- Text (broadened coverage) -----------------------------------------

    [Theory]
    [InlineData(".kt")]
    [InlineData(".swift")]
    [InlineData(".php")]
    [InlineData(".lua")]
    [InlineData(".scss")]
    [InlineData(".vue")]
    [InlineData(".tf")]
    [InlineData(".proto")]
    [InlineData(".graphql")]
    [InlineData(".zsh")]
    [InlineData(".psm1")]
    [InlineData(".rst")]
    [InlineData(".env")]
    [InlineData(".gradle")]
    [InlineData(".diff")]
    [InlineData(".svg")]
    public void Text_provider_previews_common_code_and_config_types(string extension)
        => new TextPreviewProvider().CanPreview(extension).Should().BeTrue();

    [Theory]
    [InlineData(".png")]
    [InlineData(".exe")]
    [InlineData(".zip")]
    [InlineData("")]
    public void Text_provider_rejects_binary_and_extensionless(string extension)
        => new TextPreviewProvider().CanPreview(extension).Should().BeFalse();

    [Fact]
    public async Task Text_provider_reads_a_newly_supported_type()
    {
        var provider = new TextPreviewProvider();
        string path = WriteFile("main.kt", "fun main() = println(\"hi\")");

        FilePreviewResult result = await provider.LoadAsync(path, new FilePreviewOptions(), CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.PlainText);
        result.Text.Should().Contain("fun main()");
    }

    [Fact]
    public async Task Missing_files_fail_gracefully()
    {
        string missing = Path.Combine(_dir, "gone.json");

        FilePreviewResult json = await new JsonPreviewProvider()
            .LoadAsync(missing, new FilePreviewOptions(), CancellationToken.None);
        FilePreviewResult log = await new LogPreviewProvider()
            .LoadAsync(Path.ChangeExtension(missing, ".log"), new FilePreviewOptions(), CancellationToken.None);
        FilePreviewResult md = await new MarkdownPreviewProvider()
            .LoadAsync(Path.ChangeExtension(missing, ".md"), new FilePreviewOptions(), CancellationToken.None);

        json.Kind.Should().Be(FilePreviewKind.Error);
        log.Kind.Should().Be(FilePreviewKind.Error);
        md.Kind.Should().Be(FilePreviewKind.Error);
    }
}
