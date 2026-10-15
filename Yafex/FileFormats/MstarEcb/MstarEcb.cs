#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
using System;
using System.IO;
using System.Security.Cryptography;

using Yafex.FileFormats.MstarSecure;

namespace Yafex.FileFormats.MstarEcb;

/// <summary>
/// Old-variant MStar upgrade image: the whole file is AES-128-ECB with the
/// default upgrade key, no chunk table. The plaintext is a plain MStarPkg
/// image (mboot script first). ECB blocks are independent, so the VFS view
/// decrypts randomly without chaining.
/// </summary>
public static class MstarEcb
{
    /// <summary>Staging id of the default upgrade key (single entry).</summary>
    public const string KeysId = "MSTAR_DEFAULT_UPGRADE_KEY";

    /// <summary>Probe bytes decrypted for validation (script text).</summary>
    public const int ProbeLength = 64;

    public static byte[] Decrypt(byte[] image, byte[] key)
    {
        if (image.Length == 0 || image.Length % 16 != 0)
        {
            throw new InvalidDataException("MSTAR_ECB image not block-aligned");
        }
        using var aes = Aes.Create();
        aes.BlockSize = 128;
        aes.KeySize = key.Length * 8;
        aes.Mode = CipherMode.ECB;
        aes.Key = key;
        aes.Padding = PaddingMode.None;
        return aes.CreateDecryptor().TransformFinalBlock(image, 0, image.Length);
    }

    public static byte[] DecryptHead(byte[] image, byte[] key)
    {
        if (image.Length < ProbeLength) throw new InvalidDataException("MSTAR_ECB image too small");
        var head = new byte[ProbeLength];
        Array.Copy(image, head, ProbeLength);
        using var aes = Aes.Create();
        aes.BlockSize = 128;
        aes.KeySize = key.Length * 8;
        aes.Mode = CipherMode.ECB;
        aes.Key = key;
        aes.Padding = PaddingMode.None;
        return aes.CreateDecryptor().TransformFinalBlock(head, 0, head.Length);
    }

    public static bool IsValidKey(byte[] image, byte[] key)
    {
        try
        {
            return Yafex.FileFormats.MstarSecure.MstarSecure.IsScriptText(DecryptHead(image, key));
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is CryptographicException
            || ex is ArgumentException)
        {
            return false;
        }
    }
}
