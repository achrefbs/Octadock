using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;

namespace Octadock.Core.Licensing;

/// <summary>The user-facing outcome of an activation attempt.</summary>
public enum ActivationResultKind
{
    /// <summary>Activated: a valid entitlement was issued, verified, and stored.</summary>
    Activated,

    /// <summary>The pasted text is not a well-formed Octadock license key.</summary>
    InvalidKeyFormat,

    /// <summary>The service did not recognize the key.</summary>
    KeyNotRecognized,

    /// <summary>The license is not active (revoked / refunded / disputed).</summary>
    LicenseInactive,

    /// <summary>All device slots for this license are in use.</summary>
    DeviceLimitReached,

    /// <summary>The service returned an entitlement that failed local verification (rejected, not stored).</summary>
    VerificationFailed,

    /// <summary>The license service could not be reached.</summary>
    EndpointUnavailable,

    /// <summary>An unexpected error occurred.</summary>
    Error,
}

/// <summary>The result of an activation attempt: a kind, a message for the UI, and the entitlement on success.</summary>
public sealed record ActivationResult(ActivationResultKind Kind, string Message, EntitlementPayload? Entitlement)
{
    public bool Succeeded => Kind == ActivationResultKind.Activated;
}

/// <summary>
/// Orchestrates activation (WS5, the client half of the money → key → activate loop):
/// normalizes the pasted key, asks the license service to activate this device, then
/// VERIFIES the returned entitlement locally (Ed25519 over the raw payload, this
/// device, active) before persisting it via <see cref="IEntitlementStore"/>. Because
/// verification is client-side, a hostile or misconfigured server cannot unlock the
/// app — a bad entitlement is rejected and nothing is stored.
/// </summary>
public sealed class ActivationService
{
    private static readonly Action<ILogger, Exception?> LogLicenseStatusObserverFailure =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(NotifyEntitlementStored)),
            "A license-status observer failed after entitlement storage.");

    private readonly IActivationClient _client;
    private readonly IMachineIdentity _machine;
    private readonly EntitlementEvaluator _evaluator;
    private readonly IEntitlementStore _store;
    private readonly IClock _clock;
    private readonly ILogger<ActivationService>? _logger;

    /// <summary>
    /// Raised after a verified entitlement has been durably stored. Ambient UI
    /// surfaces use this to refresh immediately after Settings, CLI, or protocol
    /// activation instead of waiting for their periodic license-status poll.
    /// </summary>
    public event EventHandler? EntitlementStored;

    public ActivationService(
        IActivationClient client,
        IMachineIdentity machine,
        EntitlementEvaluator evaluator,
        IEntitlementStore store,
        IClock clock,
        ILogger<ActivationService>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _machine = machine ?? throw new ArgumentNullException(nameof(machine));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger;
    }

    /// <summary>Activates this device against the supplied (possibly messy) license key.</summary>
    public async Task<ActivationResult> ActivateAsync(string? rawKey, CancellationToken cancellationToken = default)
    {
        if (!LicenseKeyNormalizer.IsWellFormed(rawKey))
        {
            return new ActivationResult(
                ActivationResultKind.InvalidKeyFormat,
                "That doesn't look like an Octadock license key. It should read OCTA-XXXXX-XXXXX-XXXXX-XXXXX.",
                null);
        }

        string key = LicenseKeyNormalizer.Normalize(rawKey);

        ActivationClientResponse response = await _client
            .ActivateAsync(key, _machine.MachineHash, _machine.DeviceHashVersion, cancellationToken)
            .ConfigureAwait(false);

        switch (response.Transport)
        {
            case ActivationTransport.Activated:
                return VerifyAndStore(response, key);

            case ActivationTransport.DeviceLimitReached:
                return new ActivationResult(
                    ActivationResultKind.DeviceLimitReached,
                    $"This license is already active on the maximum number of devices ({response.DeviceLimit?.ToString() ?? "the allowed limit"}). " +
                    "Deactivate another device or contact support.",
                    null);

            case ActivationTransport.LicenseNotActive:
                return new ActivationResult(
                    ActivationResultKind.LicenseInactive,
                    "This license is no longer active. If you believe this is an error, contact support.",
                    null);

            case ActivationTransport.LicenseNotFound:
                return new ActivationResult(
                    ActivationResultKind.KeyNotRecognized,
                    "We couldn't find that license key. Check for typos, or paste it straight from your purchase email.",
                    null);

            case ActivationTransport.EndpointUnavailable:
                return new ActivationResult(
                    ActivationResultKind.EndpointUnavailable,
                    "Couldn't reach the Octadock license service. Check your connection and try again.",
                    null);

            default:
                _logger?.LogWarning("Activation returned an unexpected transport result: {Message}", response.Message);
                return new ActivationResult(
                    ActivationResultKind.Error,
                    "Activation failed unexpectedly. Please try again, or contact support.",
                    null);
        }
    }

    private ActivationResult VerifyAndStore(ActivationClientResponse response, string key)
    {
        EntitlementEnvelope? envelope = EntitlementEnvelope.TryParse(response.EntitlementJson ?? string.Empty);
        if (envelope is null)
        {
            _logger?.LogWarning("Activation succeeded but the entitlement could not be parsed.");
            return VerificationFailure();
        }

        EntitlementDecision decision = _evaluator.Evaluate(envelope, _machine.MachineHash, _clock.UtcNow);
        if (decision.Status != LicenseStatus.Licensed)
        {
            _logger?.LogWarning(
                "Activation entitlement rejected during verification: {Status} — {Reason}",
                decision.Status, decision.Reason);
            return VerificationFailure();
        }

        _store.SaveEntitlement(envelope);
        NotifyEntitlementStored();
        _logger?.LogInformation("Device activated for license {Key}.", Mask(key));
        return new ActivationResult(
            ActivationResultKind.Activated, "Octadock is now licensed on this device. Thank you!", decision.Payload);
    }

    private void NotifyEntitlementStored()
    {
        Delegate[] subscribers = EntitlementStored?.GetInvocationList() ?? Array.Empty<Delegate>();
        foreach (Delegate subscriber in subscribers)
        {
            try
            {
                ((EventHandler)subscriber)(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // The verified entitlement is already stored. A stale ambient
                // badge must never turn successful activation into a failure.
                if (_logger is not null)
                {
                    LogLicenseStatusObserverFailure(_logger, ex);
                }
            }
        }
    }

    private static ActivationResult VerificationFailure()
        => new(
            ActivationResultKind.VerificationFailed,
            "The license service returned a response we couldn't verify. Nothing was changed. Please contact support.",
            null);

    private static string Mask(string key)
        => key.Length <= 9 ? "OCTA-…" : string.Concat("OCTA-…-", key.AsSpan(key.Length - 5));
}
