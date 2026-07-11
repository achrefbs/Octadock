using FluentAssertions;
using Octadock.App.Ai;
using Xunit;

namespace Octadock.App.Tests.Ai;

public sealed class AgentWorkflowCatalogTests
{
    [Theory]
    [InlineData("build", AgentWorkflowCatalog.BuildKey)]
    [InlineData("code", AgentWorkflowCatalog.BuildKey)]
    [InlineData("explain", AgentWorkflowCatalog.InvestigateKey)]
    [InlineData("compare", AgentWorkflowCatalog.VerifyKey)]
    [InlineData("structured-data", AgentWorkflowCatalog.ExtractKey)]
    [InlineData("summarize", AgentWorkflowCatalog.HandoffKey)]
    public void Resolves_legacy_and_outcome_aliases(string input, string expected)
        => AgentWorkflowCatalog.Resolve(input)!.Key.Should().Be(expected);

    [Fact]
    public void Every_workflow_has_a_concrete_job_and_verifiable_criteria()
    {
        AgentWorkflowCatalog.All.Should().HaveCount(5)
            .And.OnlyContain(item =>
                !string.IsNullOrWhiteSpace(item.Title) &&
                !string.IsNullOrWhiteSpace(item.Description) &&
                !string.IsNullOrWhiteSpace(item.Goal) &&
                item.AcceptanceCriteria.Count >= 4);
        AgentWorkflowCatalog.All.Select(item => item.Key).Should().OnlyHaveUniqueItems();
    }
}
