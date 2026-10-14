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

namespace Yafex.FileSystems.Ubi;

/// <summary>
/// UBI (Unsorted Block Images) eraseblock headers, as seen on NAND firmware
/// images (e.g. Broadcom/Askey whole-flash dumps). EC and VID headers carry
/// big-endian fields; header CRCs are IEEE CRC32 seeded with 0xFFFFFFFF and
/// stored without the final xor (verified against live images).
/// </summary>
internal static class UbiConstants
{
    public const uint EcMagic = 0x55424923; // "UBI#"
    public const uint VidMagic = 0x55424921; // "UBI!"
    public const int HeaderSize = 64;
    public const int EcCrcOff = 60;
    public const int VidCrcOff = 60;
    public const byte Version = 1;

    // Volume-table (layout volume) record stride; the observed variant is
    // [reserved u32][align u32][datapad u32][type u8][upd u8][namelen u16be]
    // [name ASCII, 128-byte field][flags u8][unused][crc], matching the
    // documented big-endian layout (short names read identically).
    public const int VtblRecordStride = 172;
    public const int VtblTypeOff = 12;
    public const int VtblNameLenOff = 14;
    public const int VtblNameOff = 16;
    public const int VtblNameMax = 128;
    public const int MaxVolumes = 128;

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
            table[i] = crc;
        }
        return table;
    }

    /// <summary>Raw IEEE CRC32 (seed ~0, no final xor), matching UBI headers.</summary>
    public static uint CrcRaw(byte[] data, int offset, int count)
    {
        uint crc = 0xFFFFFFFFu;
        int end = offset + count;
        for (int i = offset; i < end; i++)
        {
            crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }

    public static uint ReadU32BE(byte[] buf, long off)
    {
        return (uint)((buf[off] << 24) | (buf[off + 1] << 16) | (buf[off + 2] << 8) | buf[off + 3]);
    }

    public static ulong ReadU64BE(byte[] buf, long off)
    {
        return ((ulong)ReadU32BE(buf, off) << 32) | ReadU32BE(buf, off + 4);
    }

    public static bool ValidEc(byte[] image, long offset)
    {
        if (offset < 0 || offset + HeaderSize > image.Length) return false;
        if (ReadU32BE(image, offset) != EcMagic) return false;
        if (image[offset + 4] != Version) return false;
        uint vidOff = ReadU32BE(image, offset + 16);
        uint dataOff = ReadU32BE(image, offset + 20);
        if (vidOff < HeaderSize || dataOff <= vidOff || dataOff > 1 << 20) return false;
        return CrcRaw(image, (int)offset, EcCrcOff) == ReadU32BE(image, offset + EcCrcOff);
    }

    public static bool ValidVid(byte[] image, long offset)
    {
        if (offset < 0 || offset + HeaderSize > image.Length) return false;
        if (ReadU32BE(image, offset) != VidMagic) return false;
        if (image[offset + 4] != Version) return false;
        return CrcRaw(image, (int)offset, VidCrcOff) == ReadU32BE(image, offset + VidCrcOff);
    }
}
