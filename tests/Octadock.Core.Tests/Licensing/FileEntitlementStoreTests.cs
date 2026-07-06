using FluentAssertions;
using Octadock.Core.Licensing;
using Xunit;

namespace Octadock.Core.Tests.Licensing;

public sealed class FileEntitlementStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly FileEntitlementStore _store;

    public FileEntitlementStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "octadock-ent-" + Guid.NewGuid().ToString("N"));
        _store = new FileEntitlementStore(_dir);
    }

    [Fact]
    public void Entitlement_round_trips()
    {
        (EntitlementVerifier _, byte[] key) = EntitlementTestKit.NewRing();
        EntitlementEnvelope envelope = EntitlementTestKit.Sign(key, new EntitlementPayload { LicenseKey = "OCTA-RT" });

        _store.SaveEntitlement(envelope);
        EntitlementEnvelope? loaded = _store.LoadEntitlement();

        loaded.Should().NotBeNull();
        loaded!.Sig.Should().Be(envelope.Sig);
        loaded.Payload.Should().Be(envelope.Payload);
    }

    [Fact]
    public void Trial_start_round_trips()
    {
        var start = new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
        _store.SaveTrialStart(start);
        _store.LoadTrialStart().Should().Be(start);
    }

    [Fact]
    public void Missing_files_return_null()
    {
        _store.LoadEntitlement().Should().BeNull();
        _store.LoadTrialStart().Should().BeNull();
    }

    [Fact]
    public void Corrupted_entitlement_file_returns_null_rather_than_throwing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "entitlement.json"), "{ this is not valid json ");

        _store.LoadEntitlement().Should().BeNull();
    }

    [Fact]
    public void Clear_removes_the_entitlement()
    {
        (EntitlementVerifier _, byte[] key) = EntitlementTestKit.NewRing();
        _store.SaveEntitlement(EntitlementTestKit.Sign(key, new EntitlementPayload()));

        _store.ClearEntitlement();

        _store.LoadEntitlement().Should().BeNull();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}
