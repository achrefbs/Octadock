using System.Text;
using Octadock.Core.Licensing;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace Octadock.Core.Tests.Licensing;

/// <summary>Shared Ed25519 signing helpers for entitlement tests (mirrors the service signer).</summary>
internal static class EntitlementTestKit
{
    public const string KeyId = "k1";

    public static (EntitlementVerifier Verifier, byte[] PrivateKey) NewRing()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        AsymmetricCipherKeyPair pair = generator.GenerateKeyPair();
        byte[] privateKey = ((Ed25519PrivateKeyParameters)pair.Private).GetEncoded();
        byte[] publicKey = ((Ed25519PublicKeyParameters)pair.Public).GetEncoded();
        return (new EntitlementVerifier(new Dictionary<string, byte[]> { [KeyId] = publicKey }), privateKey);
    }

    public static EntitlementEnvelope Sign(byte[] privateKey, EntitlementPayload payload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(payload.ToJson());
        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(privateKey, 0));
        signer.BlockUpdate(bytes, 0, bytes.Length);
        return new EntitlementEnvelope
        {
            Schema = 1,
            KeyId = KeyId,
            Payload = Base64Url.Encode(bytes),
            Sig = Base64Url.Encode(signer.GenerateSignature()),
        };
    }
}
