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

namespace Yafex.FileFormats.Cdtm;

/// <summary>TI Titan image checksum (the web-interface trailer and the
/// per-entry checksums): MSB-first CRC-32 over the covered bytes, then the
/// covered length fed as base-256 bytes (least significant first), then
/// bitwise NOT. Reproduces the stored values of observed images byte for
/// byte (trailer and kernel entry); implemented from the verified behavior.
/// Entry checksums cover the partition bytes with trailing erase padding
/// (<c>0xFF</c>) stripped.</summary>
public static class CdtmChecksum
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i << 24;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 0x80000000) != 0 ? (c << 1) ^ 0x04C11DB7 : c << 1;
            }
            table[i] = c;
        }
        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0;
        foreach (byte b in data)
        {
            crc = (crc << 8) ^ Table[((crc >> 24) ^ b) & 0xFF];
        }
        long length = data.Length;
        while (length > 0)
        {
            crc = (crc << 8) ^ Table[(int)(((crc >> 24) ^ length) & 0xFF)];
            length >>= 8;
        }
        return ~crc;
    }
}
