using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using UnityEngine;

public static class CryptoHelper
{
    private const int KEY_SIZE = 256;
    private const int BLOCK_SIZE = 128;

    public static string GetHardwareId()
    {
        string processorId = SystemInfo.processorType;
        string deviceId = SystemInfo.deviceUniqueIdentifier;
        string combined = $"{processorId}-{deviceId}";

        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        }
    }

    public static byte[] GenerateAesKey(string hardwareId)
    {
        using (var deriveBytes = new Rfc2898DeriveBytes(hardwareId, Encoding.UTF8.GetBytes("OpenRouterSalt"), 10000, HashAlgorithmName.SHA256))
        {
            return deriveBytes.GetBytes(KEY_SIZE / 8);
        }
    }

    public static string EncryptApiKey(string apiKey, byte[] key)
    {
        using (Aes aes = Aes.Create())
        {
            aes.KeySize = KEY_SIZE;
            aes.BlockSize = BLOCK_SIZE;
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            aes.GenerateIV();
            ICryptoTransform encryptor = aes.CreateEncryptor();

            using (MemoryStream msEncrypt = new MemoryStream())
            {
                msEncrypt.Write(aes.IV, 0, aes.IV.Length);

                using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                {
                    swEncrypt.Write(apiKey);
                }

                return Convert.ToBase64String(msEncrypt.ToArray());
            }
        }
    }

    public static string DecryptApiKey(string encryptedApiKey, byte[] key)
    {
        byte[] fullCipher = Convert.FromBase64String(encryptedApiKey);

        using (Aes aes = Aes.Create())
        {
            aes.KeySize = KEY_SIZE;
            aes.BlockSize = BLOCK_SIZE;
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            byte[] iv = new byte[aes.BlockSize / 8];
            byte[] cipher = new byte[fullCipher.Length - iv.Length];

            Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
            Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);

            aes.IV = iv;

            ICryptoTransform decryptor = aes.CreateDecryptor();

            using (MemoryStream msDecrypt = new MemoryStream(cipher))
            using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
            using (StreamReader srDecrypt = new StreamReader(csDecrypt))
            {
                return srDecrypt.ReadToEnd();
            }
        }
    }
}
