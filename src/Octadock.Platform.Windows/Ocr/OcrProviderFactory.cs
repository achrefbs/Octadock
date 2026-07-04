using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Ocr;
using Octadock.Core.Settings;

namespace Octadock.Platform.Windows.Ocr;

/// <summary>
/// Resolves the best available OCR provider. Currently the shipped provider is
/// Windows.Media.Ocr; Windows AI Text Recognition and Tesseract are declared as
/// not-yet-available placeholders so the settings UI can display them.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class OcrProviderFactory : IOcrProviderFactory
{
    private readonly ILogger<OcrProviderFactory> _logger;
    private readonly IReadOnlyList<IOcrProvider> _providers;

    /// <summary>Creates the factory over the registered OCR providers.</summary>
    public OcrProviderFactory(WindowsMediaOcrProvider windowsMediaOcr, ILogger<OcrProviderFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(windowsMediaOcr);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Ordered by preference for the fallback chain.
        _providers = new IOcrProvider[] { windowsMediaOcr };
    }

    /// <inheritdoc />
    public IOcrProvider? Resolve(OcrProvider preferred)
    {
        // Preferred provider if present and available.
        foreach (IOcrProvider provider in _providers)
        {
            if (provider.Kind == preferred && provider.CheckAvailability().IsAvailable)
            {
                return provider;
            }
        }

        // First available fallback.
        foreach (IOcrProvider provider in _providers)
        {
            if (provider.CheckAvailability().IsAvailable)
            {
                _logger.LogDebug("Preferred OCR provider {Preferred} unavailable; using {Fallback}.", preferred, provider.Kind);
                return provider;
            }
        }

        _logger.LogWarning("No OCR provider is available on this machine.");
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<(OcrProvider Provider, OcrAvailability Availability)> Describe()
    {
        var described = new List<(OcrProvider, OcrAvailability)>();
        var seen = new HashSet<OcrProvider>();

        foreach (IOcrProvider provider in _providers)
        {
            described.Add((provider.Kind, provider.CheckAvailability()));
            seen.Add(provider.Kind);
        }

        // Surface the not-yet-implemented providers so the UI lists every option.
        AddPlaceholder(described, seen, OcrProvider.WindowsAiTextRecognition, "Windows AI Text Recognition is not yet implemented.");
        AddPlaceholder(described, seen, OcrProvider.Tesseract, "Tesseract OCR is not bundled in this build.");

        return described;
    }

    private static void AddPlaceholder(
        List<(OcrProvider, OcrAvailability)> list,
        HashSet<OcrProvider> seen,
        OcrProvider kind,
        string reason)
    {
        if (seen.Add(kind))
        {
            list.Add((kind, OcrAvailability.Unavailable(reason)));
        }
    }
}
