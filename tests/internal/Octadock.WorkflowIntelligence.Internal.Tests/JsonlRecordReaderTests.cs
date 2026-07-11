using FluentAssertions;

namespace Octadock.WorkflowIntelligence.Internal.Tests;

public sealed class JsonlRecordReaderTests
{
    [Fact]
    public async Task Reader_preserves_exact_byte_offsets_across_mixed_line_lengths()
    {
        using var directory = new TestDirectory();
        string file = Path.Combine(directory.Path, "records.jsonl");
        await File.WriteAllTextAsync(file, "{\"a\":1}\n{\"long\":\"évidence\"}\n");
        var records = new List<JsonlRecord>();

        await foreach (JsonlRecord record in JsonlRecordReader.ReadAsync(file)) records.Add(record);

        records.Should().HaveCount(2);
        records[0].ByteStart.Should().Be(0);
        records[1].ByteStart.Should().Be(records[0].ByteEndExclusive + 1);
        JsonlRecordReader.Decode(records[1]).Should().Be("{\"long\":\"évidence\"}");
        records[1].ByteEndExclusive.Should().Be(new FileInfo(file).Length - 1);
    }

    [Fact]
    public async Task Reader_fails_visibly_when_one_record_exceeds_the_memory_contract()
    {
        using var directory = new TestDirectory();
        string file = Path.Combine(directory.Path, "oversized.jsonl");
        await File.WriteAllTextAsync(file, new string('x', 64));

        Func<Task> action = async () =>
        {
            await foreach (JsonlRecord _ in JsonlRecordReader.ReadAsync(file, maximumRecordBytes: 8))
            {
            }
        };

        await action.Should().ThrowAsync<OversizedJsonlRecordException>();
    }
}
