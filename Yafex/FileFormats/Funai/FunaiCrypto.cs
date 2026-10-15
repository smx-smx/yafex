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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Yafex.FileFormats.Funai;

/// <summary>Shared 32-bit-key 64-bit block transform used by the Funai UPG,
/// UPG-PHL and BDP containers (ECB, trailing short block passed through).
/// Independent behavioural re-implementation from the format description:
/// single-round Feistel-like construction with per-nibble substitution and
/// the documented permutation-loop quirk (LSB of F always 0).</summary>
public static class FunaiCrypto
{
    // Eight 4-to-4 substitution tables, one per nibble position (LSB first).
    private static readonly byte[][] S =
    [
        [7, 13, 14, 3, 0, 6, 9, 10, 1, 2, 8, 5, 11, 12, 4, 15],
        [13, 8, 11, 5, 6, 15, 0, 3, 4, 7, 2, 12, 1, 10, 14, 9],
        [10, 6, 9, 0, 12, 11, 7, 13, 15, 1, 3, 14, 5, 2, 8, 4],
        [3, 15, 0, 6, 10, 1, 13, 8, 9, 4, 5, 11, 12, 7, 2, 14],
        [2, 12, 4, 1, 7, 10, 11, 6, 8, 5, 3, 15, 13, 0, 14, 9],
        [14, 11, 2, 12, 4, 7, 13, 1, 5, 0, 15, 10, 3, 9, 8, 6],
        [4, 2, 1, 11, 10, 13, 7, 8, 15, 9, 12, 5, 6, 3, 0, 14],
        [11, 8, 12, 7, 1, 14, 2, 13, 6, 15, 0, 9, 10, 4, 5, 3],
    ];

    // Bit-permutation source indices, used with the verbatim loop below.
    private static readonly byte[] P =
    [
        16, 7, 20, 21, 29, 12, 28, 17, 1, 15, 23, 26, 5, 18, 31, 10,
        2, 8, 24, 14, 0, 27, 3, 9, 19, 13, 30, 6, 22, 11, 4, 25,
    ];

    public static uint F(uint b, uint k)
    {
        uint t = b ^ k;
        uint m = 0;
        for (int i = 0; i < 8; i++)
        {
            m |= (uint)S[i][(t >> (4 * i)) & 0xF] << (4 * i);
        }
        uint f = 0;
        for (int i = 0; i < 32; i++)
        {
            uint bit = (m >> P[i]) & 1u;
            f = ((f | bit) << 1) & 0xFFFFFFFFu;
        }
        return f;
    }

    /// <summary>On-disk decryption direction: (A=bytes0-3, B=bytes4-7) maps to
    /// (B, F(B,key) XOR A). Only this direction is needed for extraction.</summary>
    public static void DecryptBlock(uint a, uint b, uint key, out uint p0, out uint p1)
    {
        p0 = b;
        p1 = F(b, key) ^ a;
    }

    /// <summary>Test-fixture encryption direction (inverse of
    /// <see cref="DecryptBlock"/>): (P0,P1) maps to (F(P0,key) XOR P1, P0).
    /// Used only to build synthetic inputs with throwaway keys.</summary>
    public static void EncryptBlock(uint p0, uint p1, uint key, out uint a, out uint b)
    {
        b = p0;
        a = F(p0, key) ^ p1;
    }

    /// <summary>ECB decryption of a buffer (full 8-byte groups from offset 0;
    /// trailing 1-7 bytes pass through unchanged). Returns a new array.</summary>
    public static byte[] Decrypt(byte[] data, uint key)
    {
        var output = new byte[data.Length];
        int full = (data.Length / 8) * 8;
        for (int at = 0; at < full; at += 8)
        {
            uint a = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
            uint b = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 4, 4));
            DecryptBlock(a, b, key, out uint p0, out uint p1);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at, 4), p0);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at + 4, 4), p1);
        }
        Array.Copy(data, full, output, full, data.Length - full);
        return output;
    }

    /// <summary>ECB encryption (inverse of <see cref="Decrypt"/>), for
    /// synthetic fixture builders only.</summary>
    public static byte[] Encrypt(byte[] data, uint key)
    {
        var output = new byte[data.Length];
        int full = (data.Length / 8) * 8;
        for (int at = 0; at < full; at += 8)
        {
            uint p0 = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
            uint p1 = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 4, 4));
            EncryptBlock(p0, p1, key, out uint a, out uint b);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at, 4), a);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at + 4, 4), b);
        }
        Array.Copy(data, full, output, full, data.Length - full);
        return output;
    }

    /// <summary>Decrypts one 8-byte file-aligned window in place into the
    /// output span (used by lazy VFS streams). Inputs shorter than 8 bytes
    /// pass through.</summary>
    public static void DecryptWindow(ReadOnlySpan<byte> cipher, Span<byte> plain, uint key)
    {
        if (cipher.Length != plain.Length) throw new ArgumentException("window length mismatch");
        int full = (cipher.Length / 8) * 8;
        for (int at = 0; at < full; at += 8)
        {
            uint a = BinaryPrimitives.ReadUInt32LittleEndian(cipher.Slice(at, 4));
            uint b = BinaryPrimitives.ReadUInt32LittleEndian(cipher.Slice(at + 4, 4));
            DecryptBlock(a, b, key, out uint p0, out uint p1);
            BinaryPrimitives.WriteUInt32LittleEndian(plain.Slice(at, 4), p0);
            BinaryPrimitives.WriteUInt32LittleEndian(plain.Slice(at + 4, 4), p1);
        }
        cipher.Slice(full).CopyTo(plain.Slice(full));
    }

    public static bool IsRestrictedByte(byte b)
    {
        return b == 0
            || (b >= (byte)'A' && b <= (byte)'Z')
            || (b >= (byte)'0' && b <= (byte)'9')
            || b == (byte)'-' || b == (byte)'_';
    }

    public static bool IsRestrictedSpan(ReadOnlySpan<byte> span)
    {
        foreach (byte b in span)
        {
            if (!IsRestrictedByte(b)) return false;
        }
        return true;
    }

    /// <summary>Display string: bytes up to the first NUL as ASCII with a
    /// lossy fallback (never throws on non-UTF8).</summary>
    public static string DisplayString(ReadOnlySpan<byte> bytes)
    {
        int nul = bytes.IndexOf((byte)0);
        var slice = nul >= 0 ? bytes.Slice(0, nul) : bytes;
        return Encoding.ASCII.GetString(slice);
    }
}

/// <summary>Shared 32-bit LE key resolution for the Funai containers: an
/// explicit `-o &lt;format&gt;:key=&lt;hex&gt;` 4-byte option wins, then the
/// named raw bundle collection in store order. Never throws.</summary>
public static class FunaiKeys
{
    public static List<uint> ResolveCandidates(
        IDictionary<string, string> args,
        KeysRepository keys,
        string collectionId,
        string optionName = "key")
    {
        var candidates = new List<uint>();
        if (args.TryGetValue(optionName, out var hex))
        {
            try
            {
                var raw = Convert.FromHexString(hex);
                if (raw.Length == 4)
                {
                    candidates.Add(BinaryPrimitives.ReadUInt32LittleEndian(raw));
                }
            }
            catch (FormatException)
            {
            }
        }
        if (keys.TryGetRawCollection(collectionId, out var entries))
        {
            foreach (var raw in entries)
            {
                if (raw.Length != 4) continue;
                candidates.Add(BinaryPrimitives.ReadUInt32LittleEndian(raw));
            }
        }
        return candidates;
    }

    public static uint? ReadU32LE(byte[] keyMaterial)
    {
        if (keyMaterial.Length != 4) return null;
        return BinaryPrimitives.ReadUInt32LittleEndian(keyMaterial);
    }
}
