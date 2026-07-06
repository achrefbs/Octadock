namespace Octadock.Core.Updates;

/// <summary>
/// Fetches the update manifest over HTTP from the configured host. The host URL is
/// founder-gated (WS1) — with none configured, <see cref="IsConfigured"/> is false and
/// the update check reports "not configured".
/// </summary>
public sealed class HttpUpdateManifestSource : IUpdateManifestSource
{
    private readonly HttpClient _http;
    private readonly string? _manifestUrl;

    public HttpUpdateManifestSource(HttpClient http, string? manifestUrl)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _manifestUrl = string.IsNullOrWhiteSpace(manifestUrl) ? null : manifestUrl.Trim();
    }

    public bool IsConfigured => _manifestUrl is not null;

    public async Task<string?> FetchAsync(CancellationToken cancellationToken)
    {
        if (_manifestUrl is null)
        {
            return null;
        }

        using HttpResponseMessage response = await _http.GetAsync(_manifestUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}
