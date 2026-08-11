using System.Security.Cryptography;

namespace W.P2P;

public class SecurityManager
{
    //Derives a shared secret key using ECDH and SHA256 hash algorithm
    public static byte[] DeriveKey(ECDiffieHellman myEcdh, byte[] theirPublicKeyBytes)
    {
        using var theirPub = ECDiffieHellman.Create();
        theirPub.ImportSubjectPublicKeyInfo(theirPublicKeyBytes, out _);
        return myEcdh.DeriveKeyFromHash(theirPub.PublicKey, HashAlgorithmName.SHA256);
    }
}