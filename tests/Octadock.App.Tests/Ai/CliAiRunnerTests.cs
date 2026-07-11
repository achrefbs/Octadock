using FluentAssertions;
using Octadock.App.Ai;
using Octadock.Core.Ai;
using Xunit;

namespace Octadock.App.Tests.Ai;

public sealed class CliAiRunnerTests
{
    [Fact]
    public void Codex_arguments_are_ephemeral_read_only_and_current()
    {
        IReadOnlyList<string> arguments = CliAiRunner.ArgumentsFor(AiCliProviderIds.Codex);

        arguments.Should().ContainInOrder("-a", "never", "exec", "--ephemeral", "--skip-git-repo-check");
        arguments.Should().ContainInOrder("--sandbox", "read-only");
        arguments.Should().Contain("--ignore-rules");
        arguments.Should().Contain("-");
        arguments.Should().NotContain("--ask-for-approval", "approval is a top-level Codex option and must precede exec");
    }

    [Fact]
    public void Claude_arguments_disable_tools_customizations_and_session_persistence()
    {
        IReadOnlyList<string> arguments = CliAiRunner.ArgumentsFor(AiCliProviderIds.Claude);

        arguments.Should().ContainInOrder("-p", "--output-format", "text");
        arguments.Should().ContainInOrder("--tools", string.Empty);
        arguments.Should().Contain("--no-session-persistence");
        arguments.Should().Contain("--safe-mode");
    }
}
