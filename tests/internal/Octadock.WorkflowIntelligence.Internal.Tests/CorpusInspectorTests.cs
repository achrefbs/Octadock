using FluentAssertions;

namespace Octadock.WorkflowIntelligence.Internal.Tests;

public sealed class CorpusInspectorTests
{
    [Fact]
    public async Task Claude_self_test_recognizes_users_assistants_and_completion_boundaries()
    {
        using var directory = new TestDirectory();
        string file = Path.Combine(directory.Path, "session.jsonl");
        await File.WriteAllLinesAsync(file,
        [
            "{\"type\":\"user\",\"sessionId\":\"s1\",\"message\":{\"role\":\"user\"}}",
            "{\"type\":\"assistant\",\"sessionId\":\"s1\",\"stopReason\":\"end_turn\",\"message\":{\"role\":\"assistant\"}}",
        ]);
        ProviderInspection result = await CorpusInspector.InspectAsync(ProviderKind.Claude, directory.Path);

        result.Healthy.Should().BeTrue();
        result.UserRecords.Should().Be(1);
        result.AssistantRecords.Should().Be(1);
        result.StartBoundaries.Should().Be(1);
        result.CompletionBoundaries.Should().Be(1);
    }

    [Fact]
    public async Task Codex_self_test_recognizes_session_and_task_boundaries()
    {
        using var directory = new TestDirectory();
        string file = Path.Combine(directory.Path, "rollout.jsonl");
        await File.WriteAllLinesAsync(file,
        [
            "{\"type\":\"session_meta\",\"payload\":{\"id\":\"s1\"}}",
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"t1\"}}",
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"user_message\"}}",
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"agent_message\"}}",
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"t1\"}}",
        ]);
        ProviderInspection result = await CorpusInspector.InspectAsync(ProviderKind.Codex, directory.Path);

        result.Healthy.Should().BeTrue();
        result.StartBoundaries.Should().Be(1);
        result.CompletionBoundaries.Should().Be(1);
        result.RecordTypes.Should().Contain("payload:task_complete");
    }

    [Fact]
    public void Sample_manifest_balances_providers_without_reading_content()
    {
        using var claude = new TestDirectory();
        using var codex = new TestDirectory();
        for (int index = 0; index < 6; index++)
        {
            File.WriteAllText(Path.Combine(claude.Path, $"c{index}.jsonl"), new string('c', index + 1));
            File.WriteAllText(Path.Combine(codex.Path, $"x{index}.jsonl"), new string('x', index + 1));
        }
        CorpusSampleManifest manifest = CorpusInspector.CreateSampleManifest(6, DateTimeOffset.UtcNow, claude.Path, codex.Path);

        manifest.Samples.Should().HaveCount(6);
        manifest.Samples.Count(sample => sample.Provider == ProviderKind.Claude).Should().Be(3);
        manifest.Samples.Count(sample => sample.Provider == ProviderKind.Codex).Should().Be(3);
    }
}
