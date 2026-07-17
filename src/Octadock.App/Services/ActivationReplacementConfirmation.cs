using System.Runtime.Versioning;
using System.Windows;
using Octadock.Core.Licensing;

namespace Octadock.App.Services;

public sealed record ActivationReplacementReview(string CurrentLicenseKey, string IncomingLicenseKey);

public interface IActivationReplacementConfirmation
{
    bool Confirm(ActivationReplacementReview review);
}

[SupportedOSPlatform("windows")]
public sealed class WpfActivationReplacementConfirmation : IActivationReplacementConfirmation
{
    public bool Confirm(ActivationReplacementReview review)
    {
        ArgumentNullException.ThrowIfNull(review);

        string message =
            $"Octadock is currently licensed with {Mask(review.CurrentLicenseKey)}.\n\n" +
            $"This activation request would replace it with {Mask(review.IncomingLicenseKey)}.\n\n" +
            "Only continue if you intentionally opened this activation link. Replace the current license?";

        return MessageBox.Show(
                   message,
                   "Replace Octadock license?",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning,
                   MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    internal static string Mask(string key)
    {
        string normalized = LicenseKeyNormalizer.Normalize(key);
        if (normalized.Length < 5)
        {
            return "Unknown license";
        }

        return $"OCTA-…-{normalized[^5..]}";
    }
}
