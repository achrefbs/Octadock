using FluentAssertions;
using Octadock.LicenseService.Stripe;
using Xunit;

namespace Octadock.LicenseService.Tests;

public class StripeSignatureVerifierTests
{
    private const string Secret = "whsec_test_secret";
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
    private const string Body = """{"id":"evt_1","type":"checkout.session.completed"}""";

    private static StripeSignatureVerifier Verifier(TimeSpan? tolerance = null)
        => new(new FixedTimeProvider(Now), tolerance);

    [Fact]
    public void Valid_signature_is_accepted()
    {
        string header = StripeSignatureFactory.Make(Body, Secret, Now.ToUnixTimeSeconds());

        Verifier().Verify(Body, header, Secret).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Tampered_payload_is_rejected()
    {
        string header = StripeSignatureFactory.Make(Body, Secret, Now.ToUnixTimeSeconds());

        SignatureVerificationResult result = Verifier().Verify(Body + " ", header, Secret);

        result.IsValid.Should().BeFalse();
        result.Failure.Should().Be(SignatureFailure.NoMatchingSignature);
    }

    [Fact]
    public void Wrong_secret_is_rejected()
    {
        string header = StripeSignatureFactory.Make(Body, "whsec_attacker", Now.ToUnixTimeSeconds());

        Verifier().Verify(Body, header, Secret).Failure.Should().Be(SignatureFailure.NoMatchingSignature);
    }

    [Fact]
    public void Missing_header_is_rejected()
        => Verifier().Verify(Body, null, Secret).Failure.Should().Be(SignatureFailure.MissingHeader);

    [Fact]
    public void Missing_secret_is_rejected()
        => Verifier().Verify(Body, "t=1,v1=deadbeef", Array.Empty<string>())
            .Failure.Should().Be(SignatureFailure.MissingSecret);

    [Fact]
    public void Malformed_header_is_rejected()
        => Verifier().Verify(Body, "not-a-real-header", Secret)
            .Failure.Should().Be(SignatureFailure.MalformedHeader);

    [Fact]
    public void Stale_timestamp_outside_tolerance_is_rejected()
    {
        long staleTs = Now.AddMinutes(-10).ToUnixTimeSeconds();
        string header = StripeSignatureFactory.Make(Body, Secret, staleTs);

        Verifier(tolerance: TimeSpan.FromMinutes(5))
            .Verify(Body, header, Secret)
            .Failure.Should().Be(SignatureFailure.TimestampOutOfTolerance);
    }

    [Fact]
    public void Second_secret_during_rotation_is_accepted()
    {
        string header = StripeSignatureFactory.Make(Body, "whsec_new", Now.ToUnixTimeSeconds());

        StripeSignatureVerifier verifier = Verifier();
        verifier.Verify(Body, header, new[] { "whsec_old", "whsec_new" }).IsValid.Should().BeTrue();
    }
}
