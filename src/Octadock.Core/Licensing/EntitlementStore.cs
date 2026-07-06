using System.Text.Json;

namespace Octadock.Core.Licensing;

/// <summary>
/// Persists the activated entitlement and the trial start OUTSIDE octadock.db (WS4,
/// R11) — under <c>%LOCALAPPDATA%\Octadock\license\</c>. Because the entitlement is
/// a signed envelope, editing or injecting it fails verification and never unlocks;
/// and because it lives outside the app database, corrupting octadock.db cannot drop
/// a licensed app back to trial.
/// </summary>
public interface IEntitlementStore
{
    EntitlementEnvelope? LoadEntitlement();

    void SaveEntitlement(EntitlementEnvelope envelope);

    void ClearEntitlement();

    DateTimeOffset? LoadTrialStart();

    void SaveTrialStart(DateTimeOffset startUtc);
}

/// <inheritdoc />
public sealed class FileEntitlementStore : IEntitlementStore
{
    private static readonly JsonSerializerOptions Options = new();
    private readonly string _entitlementPath;
    private readonly string _trialPath;

    public FileEntitlementStore(string licenseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseDirectory);
        _entitlementPath = Path.Combine(licenseDirectory, "entitlement.json");
        _trialPath = Path.Combine(licenseDirectory, "trial.json");
    }

    public EntitlementEnvelope? LoadEntitlement()
    {
        try
        {
            return File.Exists(_entitlementPath)
                ? EntitlementEnvelope.TryParse(File.ReadAllText(_entitlementPath))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void SaveEntitlement(EntitlementEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        Directory.CreateDirectory(Path.GetDirectoryName(_entitlementPath)!);
        File.WriteAllText(_entitlementPath, envelope.ToJson());
    }

    public void ClearEntitlement()
    {
        try
        {
            if (File.Exists(_entitlementPath))
            {
                File.Delete(_entitlementPath);
            }
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    public DateTimeOffset? LoadTrialStart()
    {
        try
        {
            if (!File.Exists(_trialPath))
            {
                return null;
            }

            TrialRecord? record = JsonSerializer.Deserialize<TrialRecord>(File.ReadAllText(_trialPath), Options);
            return record?.TrialStartUtc;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void SaveTrialStart(DateTimeOffset startUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_trialPath)!);
        File.WriteAllText(_trialPath, JsonSerializer.Serialize(new TrialRecord(startUtc), Options));
    }

    private sealed record TrialRecord(DateTimeOffset TrialStartUtc);
}
