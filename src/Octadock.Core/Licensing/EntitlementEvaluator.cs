namespace Octadock.Core.Licensing;

/// <summary>The entitlement decision a device reaches for a stored envelope.</summary>
public enum LicenseStatus
{
    /// <summary>No entitlement present — the app is in trial territory.</summary>
    None,

    /// <summary>A valid, active, this-device entitlement.</summary>
    Licensed,

    /// <summary>Signature verified but the entitlement was refunded/disputed → revoked.</summary>
    Revoked,

    /// <summary>Signature did not verify (forged, edited, or wrong signing key).</summary>
    InvalidSignature,

    /// <summary>Valid entitlement, but issued for a different machine.</summary>
    WrongDevice,

    /// <summary>Signature verified but the payload could not be parsed.</summary>
    Malformed,
}

/// <summary>Outcome of evaluating a stored entitlement.</summary>
/// <param name="Status">The resolved status.</param>
/// <param name="Payload">The verified payload, when the signature checked out.</param>
/// <param name="UpdatesExpired">True when licensed but past the update window (still works).</param>
/// <param name="Reason">Human-readable reason (for logs/UI).</param>
public sealed record EntitlementDecision(
    LicenseStatus Status, EntitlementPayload? Payload, bool UpdatesExpired, string Reason)
{
    public bool IsLicensed => Status == LicenseStatus.Licensed;
}

/// <summary>
/// Turns a stored entitlement envelope into a trustworthy decision (WS5, R11). A
/// forged or edited envelope fails the signature check and never unlocks; an
/// entitlement for another machine is rejected; a refunded/disputed one is revoked.
/// A valid license keeps working even after its update window ends.
/// </summary>
public sealed class EntitlementEvaluator
{
    private readonly EntitlementVerifier _verifier;

    public EntitlementEvaluator(EntitlementVerifier verifier)
        => _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));

    public EntitlementDecision Evaluate(EntitlementEnvelope? envelope, string thisMachineHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thisMachineHash);

        if (envelope is null)
        {
            return new EntitlementDecision(LicenseStatus.None, null, false, "No entitlement present.");
        }

        if (!_verifier.TryVerify(envelope, out byte[] payloadBytes))
        {
            return new EntitlementDecision(
                LicenseStatus.InvalidSignature, null, false, "Entitlement signature did not verify.");
        }

        EntitlementPayload? payload = EntitlementPayload.TryParse(payloadBytes);
        if (payload is null)
        {
            return new EntitlementDecision(LicenseStatus.Malformed, null, false, "Entitlement payload is malformed.");
        }

        if (!string.Equals(payload.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return new EntitlementDecision(LicenseStatus.Revoked, payload, false, $"Entitlement status is '{payload.Status}'.");
        }

        if (!string.Equals(payload.MachineHash, thisMachineHash, StringComparison.OrdinalIgnoreCase))
        {
            return new EntitlementDecision(
                LicenseStatus.WrongDevice, payload, false, "Entitlement was issued for a different device.");
        }

        bool updatesExpired = payload.UpdatesUntil is { } until && now > until;
        return new EntitlementDecision(
            LicenseStatus.Licensed, payload, updatesExpired,
            updatesExpired ? "Licensed (update window has ended; the app keeps working)." : "Licensed.");
    }
}
