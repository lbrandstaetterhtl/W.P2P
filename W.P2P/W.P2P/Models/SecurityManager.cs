using System;
using System.Security.Cryptography;
using Microsoft.VisualBasic;

namespace W.P2P.Models;

public class SecurityManager
{
    //Derives a shared secret key using ECDH and SHA256 hash algorithm
    public static byte[] DeriveKey(ECDiffieHellman myEcdh, byte[] theirPublicKeyBytes)
    {
        using var theirPub = ECDiffieHellman.Create();
        theirPub.ImportSubjectPublicKeyInfo(theirPublicKeyBytes, out _);
        return myEcdh.DeriveKeyFromHash(theirPub.PublicKey, HashAlgorithmName.SHA256);
    }

    public static byte[] Encrypt(byte[] data, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        
        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);
        
        byte[] result = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);
        return result;
    }

    public static byte[] Decrypt(byte[] data, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        //iv is Initialization Vector
        //16 bytes is lenght of iv
        byte[] iv = new byte[16];
        Buffer.BlockCopy(data, 0, iv, 0, iv.Length);
        aes.IV = iv;
        
        byte[] encrypted = new byte[data.Length - iv.Length];
        Buffer.BlockCopy(data, iv.Length, encrypted, 0, encrypted.Length);
        
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
    }
}