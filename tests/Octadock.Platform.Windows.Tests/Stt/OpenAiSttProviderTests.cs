using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.Platform.Windows.Stt;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Stt;

/// <summary>
/// WS7 (R7): cloud transcription must activate only on the Octadock-scoped
/// OCTADOCK_OPENAI_API_KEY. A bare OPENAI_API_KEY that other dev tools set must
/// never silently enable sending audio to the cloud.
/// </summary>
public sealed class OpenAiSttProviderTests
{
    private const string Bare = "OPENAI_API_KEY";
    private const string Scoped = "OCTADOCK_OPENAI_API_KEY";

    [Fact]
    public void Bare_OPENAI_API_KEY_alone_does_not_enable_cloud()
    {
        string? savedBare = Environment.GetEnvironmentVariable(Bare);
        string? savedScoped = Environment.GetEnvironmentVariable(Scoped);
        try
        {
            Environment.SetEnvironmentVariable(Scoped, null);
            Environment.SetEnvironmentVariable(Bare, "sk-should-be-ignored");

            var provider = new OpenAiSttProvider(NullLogger<OpenAiSttProvider>.Instance);

            provider.IsAvailable.Should().BeFalse("a bare OPENAI_API_KEY must not enable cloud STT");
            provider.UnavailableReason.Should().Contain(Scoped);
            provider.UnavailableReason.Should().Contain("ignored");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Bare, savedBare);
            Environment.SetEnvironmentVariable(Scoped, savedScoped);
        }
    }

    [Fact]
    public void Scoped_OCTADOCK_OPENAI_API_KEY_enables_cloud()
    {
        string? savedBare = Environment.GetEnvironmentVariable(Bare);
        string? savedScoped = Environment.GetEnvironmentVariable(Scoped);
        try
        {
            Environment.SetEnvironmentVariable(Bare, null);
            Environment.SetEnvironmentVariable(Scoped, "sk-explicit-optin");

            var provider = new OpenAiSttProvider(NullLogger<OpenAiSttProvider>.Instance);

            provider.IsAvailable.Should().BeTrue();
            provider.UnavailableReason.Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Bare, savedBare);
            Environment.SetEnvironmentVariable(Scoped, savedScoped);
        }
    }
}
