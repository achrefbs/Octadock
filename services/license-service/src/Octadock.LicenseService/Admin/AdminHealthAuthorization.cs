using System.Security.Cryptography;
using System.Text;

namespace Octadock.LicenseService.Admin;

internal enum AdminHealthAuthorizationResult
{
    Authorized,
    Forbidden,
    Unavailable,
}

internal static class AdminHealthAuthorization
{
    public static AdminHealthAuthorizationResult Evaluate(
        string configuredToken,
        string? presentedToken)
    {
        if (string.IsNullOrWhiteSpace(configuredToken))
        {
            return AdminHealthAuthorizationResult.Unavailable;
        }

        if (string.IsNullOrEmpty(presentedToken))
        {
            return AdminHealthAuthorizationResult.Forbidden;
        }

        byte[] configuredBytes = Encoding.UTF8.GetBytes(configuredToken);
        byte[] presentedBytes = Encoding.UTF8.GetBytes(presentedToken);
        return CryptographicOperations.FixedTimeEquals(configuredBytes, presentedBytes)
            ? AdminHealthAuthorizationResult.Authorized
            : AdminHealthAuthorizationResult.Forbidden;
    }
}
