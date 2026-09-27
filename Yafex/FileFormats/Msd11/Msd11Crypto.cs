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
using System.Security.Cryptography;

namespace Yafex.FileFormats.Msd11;

/// <summary>Samsung Tizen AES framing: OpenSSL-style "Salted__" blobs whose
/// key is the passphrase itself (16 bytes: AES-128, 32 bytes: AES-256) and
/// whose IV is md5/sha256(salt). Members reuse the same cipher raw (no
/// prefix) with the per-item TOC salt.</summary>
public static class Msd11Crypto
{
    private static readonly byte[] SaltedPrefix = new byte[] {
        (byte)'S', (byte)'a', (byte)'l', (byte)'t', (byte)'e', (byte)'d',
        (byte)'_', (byte)'_' };

    public static bool IsSaltedBlob(byte[] data)
    {
        if (data.Length < 16) return false;
        for (int i = 0; i < SaltedPrefix.Length; i++)
        {
            if (data[i] != SaltedPrefix[i]) return false;
        }
        return true;
    }

    public static byte[] DecryptSalted(byte[] data, byte[] passphrase)
    {
        if (!IsSaltedBlob(data)) throw new InvalidOperationException("not a Salted__ blob");
        var salt = new byte[8];
        Array.Copy(data, 8, salt, 0, 8);
        var body = new byte[data.Length - 16];
        Array.Copy(data, 16, body, 0, body.Length);
        return DecryptRaw(body, passphrase, salt);
    }

    public static byte[] DecryptRaw(byte[] data, byte[] passphrase, byte[] salt)
    {
        if (data.Length == 0 || (data.Length & 15) != 0)
        {
            throw new InvalidOperationException("ciphertext is not block-aligned");
        }
        using var aes = CreateAes(passphrase, salt);
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(data, 0, data.Length);
    }

    /// <summary>AES-CBC instance with the Tizen salt-derived IV (PKCS7).
    /// Shared by whole-buffer decrypts and random-access member streams.</summary>
    public static Aes CreateAes(byte[] passphrase, byte[] salt)
    {
        var aes = Aes.Create();
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        if (passphrase.Length == 16)
        {
            aes.KeySize = 128;
            aes.Key = passphrase;
        }
        else if (passphrase.Length == 32)
        {
            aes.KeySize = 256;
            aes.Key = passphrase;
        }
        else
        {
            throw new InvalidOperationException("passphrase must be 16 or 32 bytes");
        }
        aes.IV = DeriveIv(passphrase, salt);
        return aes;
    }

    public static byte[] DeriveIv(byte[] passphrase, byte[] salt)
    {
        if (passphrase.Length == 16) return MD5.HashData(salt);
        if (passphrase.Length == 32)
        {
            var digest = SHA256.HashData(salt);
            var iv = new byte[16];
            Array.Copy(digest, iv, 16);
            return iv;
        }
        throw new InvalidOperationException("passphrase must be 16 or 32 bytes");
    }

    public static bool LooksLikeOuith(byte[] data)
    {
        if (data.Length < 568) return false;
        return HasMagic(data, 256, "Tizen Software Upgrade Tree Binary Format ver. 1.8")
            || HasMagic(data, 262, "Tizen Software Upgrade Tree Binary Format ver. 1.9")
            || HasMagic(data, 518, "Tizen Software Upgrade Tree Binary Format ver. 1.9");
    }

    private static bool HasMagic(byte[] data, int offset, string magic)
    {
        if (offset + magic.Length > data.Length) return false;
        for (int i = 0; i < magic.Length; i++)
        {
            if (data[offset + i] != (byte)magic[i]) return false;
        }
        return true;
    }
}
