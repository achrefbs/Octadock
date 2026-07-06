using System.Text;
using FluentAssertions;
using Octadock.Core.Licensing;
using Octadock.Core.Tests.Licensing;
using Octadock.Core.Updates;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Xunit;

namespace Octadock.Core.Tests.Updates;

/// <summary>The update check detects newer signed releases, never downgrades, and rejects unsigned/tampered manifests (WS1).</summary>
public class UpdateCheckServiceTests
{
    private static readonly ReleaseVersion Current = new(0, 2, 0, "alpha.0");

    private sealed class FakeSource(string? raw, bool configured = true) : IUpdateManifestSource
    {
        public bool IsConfigured => configured;

        public Task<string?> FetchAsync(CancellationToken cancellationToken) => Task.FromResult(raw);
    }

    private sealed class ThrowingSource : IUpdateManifestSource
    {
        public bool IsConfigured => true;

        public Task<string?> FetchAsync(CancellationToken cancellationToken)
            => throw new HttpRequestException("host unreachable");
    }

    [Fact]
    public async Task Reports_not_configured_when_no_source()
    {
        var service = new UpdateCheckService(new NullUpdateManifestSource(), Current);
        (await service.CheckAsync()).Status.Should().Be(UpdateStatus.NotConfigured);
    }

    [Fact]
    public async Task Detects_a_newer_version()
    {
        var service = new UpdateCheckService(new FakeSource("""{"version":"0.3.0","downloadUrl":"https://x/o.exe"}"""), Current);

        UpdateCheckResult result = await service.CheckAsync();

        result.Status.Should().Be(UpdateStatus.UpdateAvailable);
        result.Available.Should().Be(new ReleaseVersion(0, 3, 0, null));
        result.Manifest!.DownloadUrl.Should().Be("https://x/o.exe");
    }

    [Fact]
    public async Task Never_offers_a_downgrade()
    {
        var service = new UpdateCheckService(new FakeSource("""{"version":"0.1.0"}"""), Current);
        (await service.CheckAsync()).Status.Should().Be(UpdateStatus.UpToDate);
    }

    [Fact]
    public async Task Same_version_is_up_to_date()
    {
        var service = new UpdateCheckService(new FakeSource("""{"version":"0.2.0-alpha.0"}"""), Current);
        (await service.CheckAsync()).Status.Should().Be(UpdateStatus.UpToDate);
    }

    [Fact]
    public async Task A_fetch_failure_is_reported_not_thrown()
    {
        var service = new UpdateCheckService(new ThrowingSource(), Current);
        (await service.CheckAsync()).Status.Should().Be(UpdateStatus.CheckFailed);
    }

    [Fact]
    public async Task A_validly_signed_manifest_is_trusted()
    {
        (EntitlementVerifier verifier, byte[] privateKey) = EntitlementTestKit.NewRing();
        string signed = Sign(privateKey, """{"version":"0.4.0"}""");
        var service = new UpdateCheckService(new FakeSource(signed), Current, verifier);

        UpdateCheckResult result = await service.CheckAsync();

        result.Status.Should().Be(UpdateStatus.UpdateAvailable);
        result.Available.Should().Be(new ReleaseVersion(0, 4, 0, null));
    }

    [Fact]
    public async Task A_tampered_signed_manifest_is_rejected()
    {
        (EntitlementVerifier verifier, byte[] privateKey) = EntitlementTestKit.NewRing();
        string signed = Sign(privateKey, """{"version":"9.9.9"}""");
        EntitlementEnvelope good = EntitlementEnvelope.TryParse(signed)!;
        string tampered = (good with { Payload = good.Payload + "AA" }).ToJson();
        var service = new UpdateCheckService(new FakeSource(tampered), Current, verifier);

        (await service.CheckAsync()).Status.Should().Be(UpdateStatus.SignatureInvalid);
    }

    private static string Sign(byte[] privateKey, string manifestJson)
    {
        byte[] payload = Encoding.UTF8.GetBytes(manifestJson);
        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(privateKey, 0));
        signer.BlockUpdate(payload, 0, payload.Length);
        var envelope = new EntitlementEnvelope
        {
            Schema = 1,
            KeyId = EntitlementTestKit.KeyId,
            Payload = Base64Url.Encode(payload),
            Sig = Base64Url.Encode(signer.GenerateSignature()),
        };
        return envelope.ToJson();
    }
}
