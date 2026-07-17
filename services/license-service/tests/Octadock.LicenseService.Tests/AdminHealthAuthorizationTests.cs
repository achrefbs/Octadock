using FluentAssertions;
using Octadock.LicenseService.Admin;
using Xunit;

namespace Octadock.LicenseService.Tests;

public class AdminHealthAuthorizationTests
{
    [Fact]
    public void Missing_configured_token_fails_closed()
    {
        AdminHealthAuthorization.Evaluate(string.Empty, presentedToken: null)
            .Should().Be(AdminHealthAuthorizationResult.Unavailable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-token")]
    public void Missing_or_invalid_header_token_is_forbidden(string? presentedToken)
    {
        AdminHealthAuthorization.Evaluate("configured-token", presentedToken)
            .Should().Be(AdminHealthAuthorizationResult.Forbidden);
    }

    [Fact]
    public void Exact_header_token_preserves_authenticated_access()
    {
        AdminHealthAuthorization.Evaluate("configured-token", "configured-token")
            .Should().Be(AdminHealthAuthorizationResult.Authorized);
    }
}
