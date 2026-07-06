using Microsoft.Extensions.Logging;
using Octadock.Core.Licensing;

namespace Octadock.Core.Updates;

/// <summary>The outcome of an update check.</summary>
public enum UpdateStatus
{
    /// <summary>The running version is the latest published version.</summary>
    UpToDate,

    /// <summary>A newer signed version is available.</summary>
    UpdateAvailable,

    /// <summary>No manifest source is configured yet (host is founder-gated).</summary>
    NotConfigured,

    /// <summary>The manifest could not be fetched or parsed.</summary>
    CheckFailed,

    /// <summary>The manifest signature did not verify against the update trust ring.</summary>
    SignatureInvalid,
}

/// <summary>The result of an update check.</summary>
public sealed record UpdateCheckResult(
    UpdateStatus Status, ReleaseVersion? Available, UpdateManifest? Manifest, string Message);

/// <summary>
/// Supplies the raw update-manifest text. The live implementation fetches it from the
/// signed-manifest host, which is founder-gated (WS1); until then the null source
/// reports "not configured" rather than pretending everything is up to date.
/// </summary>
public interface IUpdateManifestSource
{
    /// <summary>True when a real manifest host is configured.</summary>
    bool IsConfigured { get; }

    /// <summary>Fetches the manifest text (a signed <c>{schema,key_id,payload,sig}</c> envelope, or raw JSON).</summary>
    Task<string?> FetchAsync(CancellationToken cancellationToken);
}

/// <summary>Default no-op source until the update-manifest host is provisioned (founder-gated).</summary>
public sealed class NullUpdateManifestSource : IUpdateManifestSource
{
    public bool IsConfigured => false;

    public Task<string?> FetchAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}

/// <summary>
/// Detects a newer released version (WS1 "update check", enough for beta; full
/// auto-update is Deferred). It never downgrades, and — when an update trust ring is
/// configured — it only trusts a manifest whose Ed25519 signature verifies over the raw
/// manifest bytes (the same <c>{schema,key_id,payload,sig}</c> envelope the entitlement
/// system uses), so a spoofed manifest can't push a hostile "update".
/// </summary>
public sealed class UpdateCheckService
{
    private readonly IUpdateManifestSource _source;
    private readonly ReleaseVersion _current;
    private readonly EntitlementVerifier? _signatureVerifier;
    private readonly ILogger<UpdateCheckService>? _logger;

    public UpdateCheckService(
        IUpdateManifestSource source,
        ReleaseVersion current,
        EntitlementVerifier? signatureVerifier = null,
        ILogger<UpdateCheckService>? logger = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _current = current;
        _signatureVerifier = signatureVerifier;
        _logger = logger;
    }

    /// <summary>The running app version this check compares against.</summary>
    public ReleaseVersion CurrentVersion => _current;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_source.IsConfigured)
        {
            return new UpdateCheckResult(
                UpdateStatus.NotConfigured, null, null,
                "Update checks aren't configured yet.");
        }

        string? raw;
        try
        {
            raw = await _source.FetchAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Update check could not reach the manifest host.");
            return new UpdateCheckResult(UpdateStatus.CheckFailed, null, null, "Couldn't check for updates right now.");
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new UpdateCheckResult(UpdateStatus.CheckFailed, null, null, "Couldn't check for updates right now.");
        }

        UpdateManifest? manifest = ResolveManifest(raw, out bool signatureInvalid);
        if (signatureInvalid)
        {
            return new UpdateCheckResult(
                UpdateStatus.SignatureInvalid, null, null,
                "The update manifest wasn't signed by a trusted key and was ignored.");
        }

        if (manifest is null || !ReleaseVersion.TryParse(manifest.Version, out ReleaseVersion latest))
        {
            return new UpdateCheckResult(UpdateStatus.CheckFailed, null, null, "The update manifest was unreadable.");
        }

        return latest.CompareTo(_current) > 0
            ? new UpdateCheckResult(UpdateStatus.UpdateAvailable, latest, manifest, $"Octadock {latest} is available.")
            : new UpdateCheckResult(UpdateStatus.UpToDate, _current, manifest, "You're on the latest version.");
    }

    private UpdateManifest? ResolveManifest(string raw, out bool signatureInvalid)
    {
        signatureInvalid = false;

        if (_signatureVerifier is null)
        {
            // No update trust ring configured: treat the manifest as advisory. The
            // installer's SHA-256 + code signature remain the integrity gate.
            return UpdateManifest.TryParse(raw);
        }

        EntitlementEnvelope? envelope = EntitlementEnvelope.TryParse(raw);
        if (envelope is null || !_signatureVerifier.TryVerify(envelope, out byte[] payload))
        {
            signatureInvalid = true;
            return null;
        }

        return UpdateManifest.TryParse(payload);
    }
}
