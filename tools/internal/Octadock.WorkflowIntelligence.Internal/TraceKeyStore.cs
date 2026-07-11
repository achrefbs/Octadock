using System.Security.Cryptography;

namespace Octadock.WorkflowIntelligence.Internal;

internal sealed class TraceKeyStore
{
    private const int KeySize = 32;
    private readonly TracePaths _paths;

    internal TraceKeyStore(TracePaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    internal byte[] GetOrCreateKey()
    {
        Directory.CreateDirectory(_paths.Root);
        if (File.Exists(_paths.KeyPath))
        {
            return ReadKey();
        }

        byte[] key = RandomNumberGenerator.GetBytes(KeySize);
        byte[] protectedKey = WindowsDpapi.Protect(key);
        try
        {
            using var stream = new FileStream(
                _paths.KeyPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4_096,
                FileOptions.WriteThrough);
            stream.Write(protectedKey);
            stream.Flush(flushToDisk: true);
            return key;
        }
        catch (IOException) when (File.Exists(_paths.KeyPath))
        {
            CryptographicOperations.ZeroMemory(key);
            return ReadKey();
        }
    }

    private byte[] ReadKey()
    {
        byte[] protectedKey = File.ReadAllBytes(_paths.KeyPath);
        byte[] key = WindowsDpapi.Unprotect(protectedKey);
        if (key.Length != KeySize)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new CryptographicException("The internal trace key has an invalid length.");
        }

        return key;
    }
}
