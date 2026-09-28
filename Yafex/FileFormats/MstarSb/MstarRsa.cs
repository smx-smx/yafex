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
// MStar RSA/SHA256 verification, ported with permission from
// smx-smx/sb_verify (Author: Smx). Rewritten against
// Portable.BouncyCastle (already a Yafex dependency).
using System;
using System.IO;
using System.Linq;

using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Utilities.Encoders;

namespace Yafex.FileFormats.MstarSb;

/// <summary>Raw RSA + PKCS#1 v1.5 SHA256 prefix check, returning the digest.</summary>
public static class MstarRsa
{
    private static readonly byte[] Pkcs1Sha256Prefix =
        Hex.Decode("3031300d060960864801650304020105000420");

    public static byte[] Decrypt(byte[] modulus, byte[] exponent, byte[] signature)
    {
        var key = new RsaKeyParameters(false,
            new BigInteger(1, modulus),
            new BigInteger(1, exponent));
        var rsa = new RsaEngine();
        rsa.Init(false, key);
        var padded = new Pkcs1Encoding(rsa);
        byte[] decrypted;
        try
        {
            decrypted = padded.ProcessBlock(signature, 0, signature.Length);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("RSA decrypt failed", ex);
        }
        if (decrypted.Length < Pkcs1Sha256Prefix.Length
            || !decrypted.Take(Pkcs1Sha256Prefix.Length).SequenceEqual(Pkcs1Sha256Prefix))
        {
            throw new InvalidDataException("missing PKCS#1 SHA256 prefix");
        }
        var digest = new byte[decrypted.Length - Pkcs1Sha256Prefix.Length];
        Array.Copy(decrypted, Pkcs1Sha256Prefix.Length, digest, 0, digest.Length);
        return digest;
    }

    public static bool Verify(byte[] modulus, byte[] exponent, byte[] data, byte[] signature)
    {
        try
        {
            byte[] expected = MstarSb.Sha256(data);
            byte[] actual = Decrypt(modulus, exponent, signature);
            return expected.SequenceEqual(actual);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
