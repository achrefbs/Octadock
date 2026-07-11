using FluentAssertions;

namespace Octadock.WorkflowIntelligence.Internal.Tests;

public sealed class IncrementalJsonlTailerTests
{
    [Fact]
    public async Task Tail_advances_only_through_complete_lines_then_resumes_partial_line()
    {
        using var directory = new TestDirectory();
        string path = Path.Combine(directory.Path, "session.jsonl");
        await File.WriteAllTextAsync(path, "{\"type\":\"user\"}\n{\"type\":\"assist");

        IncrementalTailBatch first = await IncrementalJsonlTailer.ReadAsync(path, 0);

        first.Lines.Should().ContainSingle().Which.Should().Be("{\"type\":\"user\"}");
        first.HasIncompleteLine.Should().BeTrue();
        first.NextOffset.Should().BeGreaterThan(0).And.BeLessThan(first.FileLength);

        await File.AppendAllTextAsync(path, "ant\"}\n");
        IncrementalTailBatch second = await IncrementalJsonlTailer.ReadAsync(path, first.NextOffset);

        second.Lines.Should().ContainSingle().Which.Should().Be("{\"type\":\"assistant\"}");
        second.NextOffset.Should().Be(second.FileLength);
        second.HasIncompleteLine.Should().BeFalse();
    }

    [Fact]
    public async Task Tail_resets_after_truncation()
    {
        using var directory = new TestDirectory();
        string path = Path.Combine(directory.Path, "rollout.jsonl");
        await File.WriteAllTextAsync(path, "{\"type\":\"one\"}\n");
        long staleOffset = new FileInfo(path).Length + 100;
        await File.WriteAllTextAsync(path, "{\"type\":\"two\"}\n");

        IncrementalTailBatch batch = await IncrementalJsonlTailer.ReadAsync(path, staleOffset);

        batch.ResetAfterTruncation.Should().BeTrue();
        batch.EffectiveOffset.Should().Be(0);
        batch.Lines.Should().ContainSingle().Which.Should().Be("{\"type\":\"two\"}");
    }

    [Fact]
    public async Task Cursor_persists_no_plaintext_path_or_transcript_content()
    {
        using var directory = new TestDirectory();
        var paths = new TracePaths(Path.Combine(directory.Path, "store"));
        string transcript = Path.Combine(directory.Path, "secret-project-name.jsonl");
        await File.WriteAllTextAsync(transcript, "classified transcript text\n");
        var cursors = new TailCursorStore(paths);

        await cursors.SaveAsync(ProviderKind.Claude, transcript, File.GetCreationTimeUtc(transcript), 12);

        string cursorText = await File.ReadAllTextAsync(Directory.GetFiles(paths.CursorsPath).Single());
        cursorText.Should().NotContain("secret-project-name");
        cursorText.Should().NotContain("classified transcript text");
        cursors.Load(ProviderKind.Claude, transcript)!.Offset.Should().Be(12);
    }

    [Fact]
    public async Task Tail_reports_an_oversized_record_without_advancing_the_cursor()
    {
        using var directory = new TestDirectory();
        string path = Path.Combine(directory.Path, "oversized.jsonl");
        await File.WriteAllTextAsync(path, new string('x', 32));

        IncrementalTailBatch batch = await IncrementalJsonlTailer.ReadAsync(path, 0, maxBytes: 8);

        batch.BlockedByOversizedLine.Should().BeTrue();
        batch.NextOffset.Should().Be(0);
        batch.Lines.Should().BeEmpty();
    }
}
