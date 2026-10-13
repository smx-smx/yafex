#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Yafex.FileFormats.Tli;

/// <summary>
/// Image payload decryption for encrypted TLI members: OpenSSL-style
/// "Salted__" envelope, AES-256-CBC, key/IV from EVP_BytesToKey (MD5,
/// one iteration) over the password. Passwords resolve like other
/// formats: explicit option first, then the "tli-password" raw bundle
/// entry; without passwords (or on failure) members pass through raw.
/// </summary>
public static class TliCrypto
{
    public const string PasswordId = "tli-password";

    private static readonly byte[] SaltedMagic =
    {
        (byte)'S', (byte)'a', (byte)'l', (byte)'t',
        (byte)'e', (byte)'d', (byte)'_', (byte)'_',
    };

    public static bool IsEncrypted(byte[] blob)
    {
        if (blob.Length < 16)
        {
            return false;
        }
        for (int i = 0; i < SaltedMagic.Length; i++)
        {
            if (blob[i] != SaltedMagic[i])
            {
                return false;
            }
        }
        return true;
    }

    public static void EvpBytesToKey(byte[] password, byte[] salt, byte[] key, byte[] iv)
    {
        using var md5 = MD5.Create();
        var stream = new byte[key.Length + iv.Length];
        byte[] prev = [];
        int done = 0;
        while (done < stream.Length)
        {
            var input = new byte[prev.Length + password.Length + salt.Length];
            Buffer.BlockCopy(prev, 0, input, 0, prev.Length);
            Buffer.BlockCopy(password, 0, input, prev.Length, password.Length);
            Buffer.BlockCopy(salt, 0, input, prev.Length + password.Length, salt.Length);
            prev = md5.ComputeHash(input);
            int take = Math.Min(prev.Length, stream.Length - done);
            Buffer.BlockCopy(prev, 0, stream, done, take);
            done += take;
        }
        Buffer.BlockCopy(stream, 0, key, 0, key.Length);
        Buffer.BlockCopy(stream, key.Length, iv, 0, iv.Length);
    }

    public static bool TryDecrypt(byte[] blob, IReadOnlyList<byte[]> passwords, out byte[] plain)
    {
        plain = [];
        if (!IsEncrypted(blob) || passwords.Count == 0)
        {
            return false;
        }
        var salt = new byte[8];
        Buffer.BlockCopy(blob, 8, salt, 0, 8);
        int cipherLength = blob.Length - 16;
        if (cipherLength <= 0 || cipherLength % 16 != 0)
        {
            return false;
        }
        foreach (var password in passwords)
        {
            if (password.Length == 0)
            {
                continue;
            }
            try
            {
                var key = new byte[32];
                var iv = new byte[16];
                EvpBytesToKey(password, salt, key, iv);
                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;
                using var dec = aes.CreateDecryptor();
                var cipher = new byte[cipherLength];
                Buffer.BlockCopy(blob, 16, cipher, 0, cipherLength);
                plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                return true;
            }
            catch (CryptographicException)
            {
                continue;
            }
        }
        plain = [];
        return false;
    }
}
