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
using System.IO;
using System.Text;

using Yafex.Support;

namespace Yafex.FileFormats.UImage;

/// <summary>
/// U-Boot legacy image header (64 bytes, big-endian). Only the strip is
/// implemented here: inner payload keeps its own encoding (often LG-LZO
/// or gzip on these firmwares) and is processed further.
/// </summary>
public static class UImage
{
    public const uint Magic = 0x27051956;
    public const int HeaderSize = 64;

    public sealed class Header
    {
        public uint Time;
        public uint Size;
        public uint Load;
        public uint Entry;
        public byte Os;
        public byte Arch;
        public byte Type;
        public byte Comp;
        public string Name = "";
    }

    private static uint ReadU32BE(byte[] buf, int off)
    {
        return ((uint)buf[off] << 24) | ((uint)buf[off + 1] << 16) | ((uint)buf[off + 2] << 8) | buf[off + 3];
    }

    public static uint Crc32(byte[] buf, int off, int len)
    {
        return Support.Crc32.Compute(buf, off, len);
    }

    public static bool HasHeader(byte[] prefix)
    {
        if (prefix.Length < HeaderSize || ReadU32BE(prefix, 0) != Magic)
        {
            return false;
        }
        uint want = ReadU32BE(prefix, 4);
        var copy = new byte[HeaderSize];
        Buffer.BlockCopy(prefix, 0, copy, 0, HeaderSize);
        copy[4] = copy[5] = copy[6] = copy[7] = 0;
        return Crc32(copy, 0, HeaderSize) == want;
    }

    public static bool TryReadHeader(byte[] image, out Header header)
    {
        header = new Header();
        if (image.Length < HeaderSize || !HasHeader(image))
        {
            return false;
        }
        header.Time = ReadU32BE(image, 8);
        header.Size = ReadU32BE(image, 12);
        header.Load = ReadU32BE(image, 16);
        header.Entry = ReadU32BE(image, 20);
        // data CRC is advisory (trailing segments may be stripped): not enforced
        header.Os = image[28];
        header.Arch = image[29];
        header.Type = image[30];
        header.Comp = image[31];
        int nameLen = 0;
        while (nameLen < 32 && image[32 + nameLen] != 0)
        {
            nameLen++;
        }
        header.Name = Encoding.ASCII.GetString(image, 32, nameLen);
        if ((long)header.Size > image.Length - HeaderSize)
        {
            return false;
        }
        return true;
    }

    public static byte[] Strip(byte[] image)
    {
        if (!TryReadHeader(image, out var header))
        {
            throw new InvalidDataException("not a uImage");
        }
        var inner = new byte[header.Size];
        Buffer.BlockCopy(image, HeaderSize, inner, 0, inner.Length);
        return inner;
    }
}
