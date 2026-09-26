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

namespace Yafex.FileFormats.MediatekPkgOld;

/// <summary>Old-variant MTK PKG header/data cipher.
/// Interoperable two-round Feistel cipher over 32-bit little-endian
/// blocks with ciphertext chaining: each block is decrypted, then XORed
/// with the previous ciphertext block (first block uses zero). A final
/// IV XOR covers the first four output bytes; sub-block tails are
/// masked with 0x3D. Wrong keys yield garbage, never exceptions;
/// callers validate the decrypted structure.</summary>
public static class MtkOldCrypto
{
    // 16->24 bit expansion map: output bit i samples input bit Expand[i].
    private static readonly byte[] Expand =
    [
        0x00, 0x0F, 0x0E, 0x0D, 0x0C, 0x0B, 0x0C, 0x0B,
        0x0A, 0x09, 0x08, 0x07, 0x08, 0x07, 0x06, 0x05,
        0x04, 0x03, 0x04, 0x03, 0x02, 0x01, 0x00, 0x0F,
    ];

    // Substitution pages: four pages of 64 nibbles; round i consults
    // page i with its 6-bit group value.
    private static readonly byte[] SBox =
    [
        0x0D, 0x06, 0x00, 0x0A, 0x0E, 0x03, 0x0B, 0x05,
        0x07, 0x01, 0x09, 0x04, 0x02, 0x08, 0x0C, 0x0F,
        0x00, 0x05, 0x0A, 0x03, 0x07, 0x09, 0x0C, 0x0F,
        0x0B, 0x02, 0x06, 0x0D, 0x08, 0x0E, 0x01, 0x04,
        0x08, 0x03, 0x05, 0x09, 0x0B, 0x0C, 0x06, 0x0A,
        0x01, 0x0D, 0x02, 0x0E, 0x04, 0x07, 0x0F, 0x00,
        0x07, 0x00, 0x09, 0x05, 0x0C, 0x06, 0x0A, 0x03,
        0x08, 0x0B, 0x0F, 0x02, 0x01, 0x0D, 0x04, 0x0E,
        0x09, 0x0E, 0x05, 0x00, 0x0C, 0x07, 0x06, 0x0B,
        0x02, 0x04, 0x0F, 0x03, 0x01, 0x0A, 0x08, 0x0D,
        0x0F, 0x02, 0x03, 0x09, 0x06, 0x0C, 0x08, 0x05,
        0x01, 0x0D, 0x04, 0x0A, 0x0B, 0x07, 0x0E, 0x00,
        0x05, 0x0B, 0x09, 0x06, 0x0A, 0x01, 0x00, 0x0C,
        0x0E, 0x08, 0x02, 0x0F, 0x07, 0x04, 0x0D, 0x03,
        0x0A, 0x05, 0x00, 0x0C, 0x0D, 0x02, 0x07, 0x09,
        0x04, 0x03, 0x0B, 0x06, 0x0E, 0x08, 0x01, 0x0F,
        0x0C, 0x02, 0x05, 0x0B, 0x03, 0x0E, 0x0F, 0x04,
        0x07, 0x08, 0x09, 0x06, 0x00, 0x0D, 0x0A, 0x01,
        0x07, 0x0E, 0x0A, 0x05, 0x0C, 0x02, 0x01, 0x0B,
        0x00, 0x03, 0x0F, 0x08, 0x09, 0x04, 0x06, 0x0D,
        0x01, 0x0F, 0x0B, 0x0C, 0x0E, 0x05, 0x08, 0x02,
        0x0A, 0x06, 0x04, 0x03, 0x09, 0x00, 0x07, 0x0D,
        0x08, 0x02, 0x04, 0x0B, 0x07, 0x0C, 0x0D, 0x01,
        0x05, 0x0F, 0x03, 0x06, 0x0E, 0x09, 0x00, 0x0A,
        0x0E, 0x02, 0x07, 0x0C, 0x0B, 0x05, 0x04, 0x09,
        0x08, 0x0D, 0x01, 0x0A, 0x06, 0x00, 0x0F, 0x03,
        0x04, 0x08, 0x02, 0x05, 0x0E, 0x03, 0x01, 0x0F,
        0x0D, 0x07, 0x0B, 0x0C, 0x00, 0x09, 0x06, 0x0A,
        0x09, 0x0E, 0x0A, 0x01, 0x0C, 0x02, 0x07, 0x04,
        0x03, 0x00, 0x0F, 0x06, 0x05, 0x0B, 0x08, 0x0D,
        0x0F, 0x04, 0x0C, 0x0B, 0x05, 0x08, 0x02, 0x01,
        0x0A, 0x09, 0x06, 0x00, 0x03, 0x0E, 0x0D, 0x07,
    ];

    // 16-bit permutation: output bit Permute[i] takes input bit i.
    private static readonly byte[] Permute =
    [
        0x0C, 0x08, 0x05, 0x00, 0x0A, 0x02, 0x0E, 0x07,
        0x04, 0x09, 0x01, 0x0D, 0x03, 0x06, 0x0B, 0x0F,
    ];

    private static ushort Round(ushort value, ushort subkey)
    {
        uint grownValue = 0;
        uint grownKey = 0;
        for (int i = 0; i < 24; i++)
        {
            grownValue |= (uint)(((value >> Expand[i]) & 1) << i);
            grownKey |= (uint)(((subkey >> Expand[i]) & 1) << i);
        }

        uint mixed = grownValue ^ grownKey;
        ushort folded = 0;
        for (int page = 0; page < 4; page++)
        {
            int slot = (int)((mixed >> (6 * page)) & 0x3F);
            folded |= (ushort)(SBox[64 * page + slot] << (4 * page));
        }

        ushort shuffled = 0;
        for (int i = 0; i < 16; i++)
        {
            shuffled |= (ushort)(((folded >> i) & 1) << Permute[i]);
        }
        return shuffled;
    }

    public static byte[] Decrypt(byte[] data, byte[] key, byte[] iv)
    {
        if (key.Length != 4) throw new ArgumentException("Old MTK header key is 4 bytes", nameof(key));
        if (iv.Length != 4) throw new ArgumentException("Old MTK header IV is 4 bytes", nameof(iv));

        var output = new byte[data.Length];
        uint wordKey = (uint)(key[0] | (key[1] << 8) | (key[2] << 16) | (key[3] << 24));
        ushort subHigh = (ushort)((wordKey >> 16) & 0xFFFF);
        ushort subLow = (ushort)(wordKey & 0xFFFF);

        int aligned = (data.Length / 4) * 4;
        uint chain = 0;
        for (int at = 0; at < aligned; at += 4)
        {
            uint cipher = (uint)(data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24));
            ushort lo = (ushort)(cipher & 0xFFFF);
            ushort hi = (ushort)((cipher >> 16) & 0xFFFF);
            ushort first = (ushort)(Round(lo, subHigh) ^ hi);
            ushort second = (ushort)(Round(first, subLow) ^ lo);
            uint plain = (((uint)second << 16) | first) ^ chain;
            chain = cipher;

            output[at] = (byte)(plain & 0xFF);
            output[at + 1] = (byte)((plain >> 8) & 0xFF);
            output[at + 2] = (byte)((plain >> 16) & 0xFF);
            output[at + 3] = (byte)((plain >> 24) & 0xFF);
        }

        for (int at = aligned; at < data.Length; at++)
        {
            output[at] = (byte)(data[at] ^ 0x3D);
        }

        if (data.Length >= 4)
        {
            for (int i = 0; i < 4; i++)
            {
                output[i] ^= iv[i];
            }
        }

        return output;
    }
}
