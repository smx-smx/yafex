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

namespace Yafex.FileFormats.AllwinnerImg;

/// <summary>RC6-32/20 block cipher (16-byte blocks, 20 rounds), implemented
/// from the published specification (Rivest et al.). Used by the classic
/// Allwinner LiveSuit image format: the main header, the file-entry table
/// and the part payloads are each RC6-encrypted (ECB, full 16-byte blocks)
/// under a fixed 32-byte key whose last byte selects the region
/// ('i' = header, 'm' = entry table, 'g' = payloads).</summary>
public static class AwCrypto
{
    private const uint P32 = 0xB7E15163u;
    private const uint Q32 = 0x9E3779B9u;
    private const int Rounds = 20;

    public static byte[] HeaderKey() => RegionKey(0x00, (byte)'i');
    public static byte[] EntriesKey() => RegionKey(0x01, (byte)'m');
    public static byte[] PayloadKey() => RegionKey(0x02, (byte)'g');

    private static byte[] RegionKey(byte fill, byte last)
    {
        var key = new byte[32];
        for (int i = 0; i < 31; i++) key[i] = fill;
        key[31] = last;
        return key;
    }

    private static uint Rotl(uint v, int n) => (v << (n & 31)) | (v >> ((32 - n) & 31));
    private static uint Rotr(uint v, int n) => (v >> (n & 31)) | (v << ((32 - n) & 31));

    /// <summary>Precomputes the 44 subkeys for streaming block decryption.</summary>
    public static uint[] ExpandSubkeys(byte[] key) => ExpandKey(key);

    private static uint[] ExpandKey(byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("RC6 key must be 32 bytes", nameof(key));
        // key bytes -> c little-endian words (c = 8 for 32-byte keys)
        int c = key.Length / 4;
        var l = new uint[c];
        for (int i = 0; i < c; i++)
        {
            l[i] = BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(i * 4, 4));
        }
        // subkeys: t = 2r + 4 = 44 words
        int t = 2 * Rounds + 4;
        var s = new uint[t];
        s[0] = P32;
        for (int i = 1; i < t; i++) s[i] = s[i - 1] + Q32;
        // mix key into subkeys
        uint a = 0, b = 0;
        int ii = 0, jj = 0;
        int v = 3 * Math.Max(c, t);
        for (int k = 0; k < v; k++)
        {
            a = s[ii] = Rotl(s[ii] + a + b, 3);
            b = l[jj] = Rotl(l[jj] + a + b, (int)(a + b));
            ii = (ii + 1) % t;
            jj = (jj + 1) % c;
        }
        return s;
    }

    private static uint F(uint x) => x * (2 * x + 1);

    public static void DecryptBlock(byte[] data, int offset, uint[] s)
    {
        uint a = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        uint b = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4, 4));
        uint c = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 8, 4));
        uint d = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 12, 4));

        c -= s[2 * Rounds + 3];
        a -= s[2 * Rounds + 2];
        for (int i = Rounds; i >= 1; i--)
        {
            (a, b, c, d) = (d, a, b, c);
            uint u = Rotl(F(d), 5);
            uint t = Rotl(F(b), 5);
            c = Rotr(c - s[2 * i + 1], (int)t) ^ u;
            a = Rotr(a - s[2 * i], (int)u) ^ t;
        }
        d -= s[1];
        b -= s[0];

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), a);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 4, 4), b);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 8, 4), c);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 12, 4), d);
    }

    public static void EncryptBlock(byte[] data, int offset, uint[] s)
    {
        uint a = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        uint b = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4, 4));
        uint c = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 8, 4));
        uint d = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 12, 4));

        b += s[0];
        d += s[1];
        for (int i = 1; i <= Rounds; i++)
        {
            uint t = Rotl(F(b), 5);
            uint u = Rotl(F(d), 5);
            a = Rotl(a ^ t, (int)u) + s[2 * i];
            c = Rotl(c ^ u, (int)t) + s[2 * i + 1];
            (a, b, c, d) = (b, c, d, a);
        }
        a += s[2 * Rounds + 2];
        c += s[2 * Rounds + 3];

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), a);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 4, 4), b);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 8, 4), c);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 12, 4), d);
    }

    /// <summary>ECB-decrypts whole 16-byte blocks in place; a trailing
    /// short tail (payload lengths are 16-aligned in practice) is left
    /// untouched, mirroring the reference unpacker.</summary>
    public static byte[] Decrypt(byte[] data, byte[] key)
    {
        var s = ExpandKey(key);
        var outBuf = (byte[])data.Clone();
        for (int off = 0; off + 16 <= outBuf.Length; off += 16)
        {
            DecryptBlock(outBuf, off, s);
        }
        return outBuf;
    }

    public static byte[] Encrypt(byte[] data, byte[] key)
    {
        var s = ExpandKey(key);
        var outBuf = (byte[])data.Clone();
        for (int off = 0; off + 16 <= outBuf.Length; off += 16)
        {
            EncryptBlock(outBuf, off, s);
        }
        return outBuf;
    }
}
