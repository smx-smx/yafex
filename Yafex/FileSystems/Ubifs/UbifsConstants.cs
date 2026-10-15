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

namespace Yafex.FileSystems.Ubifs;

/// <summary>
/// UBIFS on-flash structures (little-endian), as seen on NAND firmware
/// images (e.g. Broadcom/Askey rootfs volumes). Node CRCs are IEEE CRC32
/// seeded with 0xFFFFFFFF and stored without the final xor, covering the
/// node bytes after the magic+crc fields. Directory entry names carry an
/// R5 hash in the key, used here to validate parses.
/// </summary>
internal static class UbifsConstants
{
    public const uint Magic = 0x06101831;
    public const int HeaderSize = 24;

    // node types
    public const int TypeIno = 0;
    public const int TypeData = 1;
    public const int TypeDent = 2;
    public const int TypePad = 5;
    public const int TypeSb = 6;

    // key types (high 3 bits of the second key word)
    public const int KeyIno = 0;
    public const int KeyData = 1;
    public const int KeyDent = 2;

    // data compression types
    public const uint ComprNone = 0;
    public const uint ComprZlib = 2;

    // fixed offsets inside an inode node
    public const int InoSizeOff = 48;
    public const int InoModeOff = 104;
    public const int InoFixedSize = 160;

    // fixed offsets inside a direntry node
    public const int DentInoOff = 40;
    public const int DentNlenOff = 50;

    // fixed offsets inside a data node
    public const int DataSizeOff = 40;
    public const int DataComprOff = 44;
    public const int DataPayloadOff = 48;

    // data block granularity: block N of a file covers [N*4096, ...),
    // payloads never exceed one block on the wire
    public const int BlockSize = 4096;

    // superblock leb_size field
    public const int SbLebSizeOff = 36;

    /// <summary>R5 name hash used in UBIFS directory keys.</summary>
    public static uint R5Hash(byte[] name, int offset, int count)
    {
        uint a = 0;
        for (int i = offset; i < offset + count; i++)
        {
            uint c = name[i];
            a += (c << 4) + (c >> 4);
            a *= 11;
        }
        return a;
    }

    public static ushort ReadU16(byte[] buf, long off)
    {
        return (ushort)(buf[off] | (buf[off + 1] << 8));
    }

    public static uint ReadU32(byte[] buf, long off)
    {
        return (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));
    }

    public static ulong ReadU64(byte[] buf, long off)
    {
        return ReadU32(buf, off) | ((ulong)ReadU32(buf, off + 4) << 32);
    }
}
