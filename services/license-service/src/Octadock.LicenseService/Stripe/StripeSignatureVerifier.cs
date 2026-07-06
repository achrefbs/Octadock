using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Octadock.LicenseService.Stripe;

/// <summary>Why a webhook signature was rejected (or <see cref="None"/> when valid).</summary>
public enum SignatureFailure
{
    None = 0,
    MissingSecret,
    MissingHeader,
    MalformedHeader,
    TimestampOutOfTolerance,
    NoMatchingSignature,
}

/// <summary>Outcome of verifying a <c>Stripe-Signature</c> header.</summary>
public readonly record struct SignatureVerificationResult(bool IsValid, SignatureFailure Failure)
{
    public static SignatureVerificationResult Valid { get; } = new(true, SignatureFailure.None);

    public static SignatureVerificationResult Invalid(SignatureFailure failure) => new(false, failure);
}

/// <summary>
/// Verifies Stripe's <c>Stripe-Signature</c> header exactly as Stripe documents it
/// (WS3, R12): the signed payload is <c>{timestamp}.{raw body}</c>, HMAC-SHA256 with
/// the endpoint's signing secret, hex-encoded. Comparison is constant-time; the
/// timestamp must be within a tolerance window (default 5 minutes) to blunt replay.
/// Multiple secrets are supported so a secret rotation never drops live traffic.
///
/// This is a self-contained reimplementation of <c>EventUtility.ConstructEvent</c>
/// so the critical verification path has no external SDK dependency and is fully
/// unit-testable offline. It is a drop-in for Stripe.net's verifier.
/// </summary>
public sealed class StripeSignatureVerifier
{
    private readonly TimeProvider _time;
    private readonly TimeSpan _tolerance;

    public StripeSignatureVerifier(TimeProvider? time = null, TimeSpan? tolerance = null)
    {
        _time = time ?? TimeProvider.System;
        _tolerance = tolerance ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Verifies <paramref name="signatureHeader"/> against the raw request
    /// <paramref name="payload"/>. Accepts if ANY configured secret matches. The
    /// payload MUST be the exact bytes received — re-serializing invalidates it.
    /// </summary>
    public SignatureVerificationResult Verify(
        string payload, string? signatureHeader, IReadOnlyCollection<string> secrets)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(secrets);

        if (secrets.Count == 0 || secrets.All(string.IsNullOrWhiteSpace))
        {
            return SignatureVerificationResult.Invalid(SignatureFailure.MissingSecret);
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return SignatureVerificationResult.Invalid(SignatureFailure.MissingHeader);
        }

        if (!TryParseHeader(signatureHeader, out long timestamp, out List<byte[]> providedSignatures))
        {
            return SignatureVerificationResult.Invalid(SignatureFailure.MalformedHeader);
        }

        DateTimeOffset eventTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        TimeSpan drift = _time.GetUtcNow() - eventTime;
        if (drift > _tolerance || drift < -_tolerance)
        {
            return SignatureVerificationResult.Invalid(SignatureFailure.TimestampOutOfTolerance);
        }

        byte[] signedPayload = Encoding.UTF8.GetBytes(
            string.Concat(timestamp.ToString(CultureInfo.InvariantCulture), ".", payload));

        foreach (string secret in secrets)
        {
            if (string.IsNullOrWhiteSpace(secret))
            {
                continue;
            }

            byte[] expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signedPayload);
            foreach (byte[] candidate in providedSignatures)
            {
                if (candidate.Length == expected.Length &&
                    CryptographicOperations.FixedTimeEquals(candidate, expected))
                {
                    return SignatureVerificationResult.Valid;
                }
            }
        }

        return SignatureVerificationResult.Invalid(SignatureFailure.NoMatchingSignature);
    }

    /// <summary>Convenience overload for a single signing secret.</summary>
    public SignatureVerificationResult Verify(string payload, string? signatureHeader, string secret)
        => Verify(payload, signatureHeader, new[] { secret });

    private static bool TryParseHeader(string header, out long timestamp, out List<byte[]> signatures)
    {
        timestamp = 0;
        signatures = new List<byte[]>();
        bool sawTimestamp = false;

        foreach (string part in header.Split(','))
        {
            int eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            string key = part[..eq].Trim();
            string value = part[(eq + 1)..].Trim();

            if (key == "t")
            {
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out timestamp))
                {
                    return false;
                }

                sawTimestamp = true;
            }
            else if (key == "v1" && TryFromHex(value, out byte[] sig))
            {
                signatures.Add(sig);
            }
        }

        return sawTimestamp && signatures.Count > 0;
    }

    private static bool TryFromHex(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (value.Length == 0 || (value.Length & 1) == 1)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
