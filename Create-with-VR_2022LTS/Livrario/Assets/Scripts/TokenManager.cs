using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public static class TokenManager
{
    // Cambiá este salt por uno propio de tu app
    private static readonly byte[] AppSalt = Encoding.UTF8.GetBytes("Livrario::AppSalt::v1");
    private const string K_Access = "enc_access";
    private const string K_Refresh = "enc_refresh";
    private const string K_SavedAt = "enc_saved_at";

    // PBKDF2
    private const int PBKDF2_Iterations = 100_000;
    private const int KeyBytesLen = 32; // 256-bit

    private static byte[] DeriveKey()
    {
        var passphrase = SystemInfo.deviceUniqueIdentifier + "|livrario";
        using var kdf = new Rfc2898DeriveBytes(passphrase, AppSalt, PBKDF2_Iterations, HashAlgorithmName.SHA256);
        return kdf.GetBytes(KeyBytesLen);
    }

    // AES-CBC + HMAC-SHA256 (Encrypt-then-MAC)
    public static string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return "";
        var key = DeriveKey();
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        var iv = aes.IV;
        byte[] cipher;
        using (var enc = aes.CreateEncryptor())
        {
            var plainBytes = Encoding.UTF8.GetBytes(plaintext);
            cipher = enc.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        }

        using var hmac = new HMACSHA256(key);
        var macInput = new byte[iv.Length + cipher.Length];
        Buffer.BlockCopy(iv, 0, macInput, 0, iv.Length);
        Buffer.BlockCopy(cipher, 0, macInput, iv.Length, cipher.Length);
        var mac = hmac.ComputeHash(macInput);

        var outBytes = new byte[iv.Length + cipher.Length + mac.Length];
        Buffer.BlockCopy(iv, 0, outBytes, 0, iv.Length);
        Buffer.BlockCopy(cipher, 0, outBytes, iv.Length, cipher.Length);
        Buffer.BlockCopy(mac, 0, outBytes, iv.Length + cipher.Length, mac.Length);

        return Convert.ToBase64String(outBytes);
    }

    public static string Decrypt(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return "";
        var all = Convert.FromBase64String(base64);
        var key = DeriveKey();

        if (all.Length < 16 + 32) throw new Exception("Payload corto");

        var iv = new byte[16];
        Buffer.BlockCopy(all, 0, iv, 0, 16);

        var mac = new byte[32];
        Buffer.BlockCopy(all, all.Length - 32, mac, 0, 32);

        var cipherLen = all.Length - 16 - 32;
        var cipher = new byte[cipherLen];
        Buffer.BlockCopy(all, 16, cipher, 0, cipherLen);

        using (var hmac = new HMACSHA256(key))
        {
            var macInput = new byte[16 + cipherLen];
            Buffer.BlockCopy(iv, 0, macInput, 0, 16);
            Buffer.BlockCopy(cipher, 0, macInput, 16, cipherLen);
            var calc = hmac.ComputeHash(macInput);
            if (!ConstTimeEquals(mac, calc)) throw new CryptographicException("MAC inválido");
        }

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var dec = aes.CreateDecryptor();
        var plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(plain);
    }

    private static bool ConstTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    // API pública
    public static void SaveTokens(string access, string refresh)
    {
        var encAccess = Encrypt(access ?? "");
        var encRefresh = Encrypt(refresh ?? "");
        PlayerPrefs.SetString(K_Access, encAccess);
        PlayerPrefs.SetString(K_Refresh, encRefresh);
        PlayerPrefs.SetString(K_SavedAt, DateTime.UtcNow.ToString("o"));
        PlayerPrefs.Save();
        Debug.Log("[TokenManager] Tokens guardados (cifrados).");
    }

    public static bool TryLoadTokens(out string access, out string refresh)
    {
        access = refresh = "";
        if (!PlayerPrefs.HasKey(K_Access) || !PlayerPrefs.HasKey(K_Refresh))
            return false;

        try
        {
            access = Decrypt(PlayerPrefs.GetString(K_Access));
            refresh = Decrypt(PlayerPrefs.GetString(K_Refresh));
            return !string.IsNullOrEmpty(access);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[TokenManager] Error descifrando: {e.Message}. Limpio almacenamiento.");
            ClearTokens();
            return false;
        }
    }

    public static void ClearTokens()
    {
        PlayerPrefs.DeleteKey(K_Access);
        PlayerPrefs.DeleteKey(K_Refresh);
        PlayerPrefs.DeleteKey(K_SavedAt);
        PlayerPrefs.Save();
        Debug.Log("[TokenManager] Tokens borrados.");
    }
}
