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

namespace Yafex.FileFormats.Idb;

/// <summary>
/// LG "16" image decoder, version 0, paletted subset (IDB blobs). Little-
/// endian header: magic, version, dimensions, palette entry count, palette
/// (16-bit RGB565 little-endian entries), big-endian pixel count, then
/// [count][index] run pairs covering exactly width*height pixels with all
/// indices inside the palette. Anything else (other versions, run variants
/// with markers/tails, direct pixels) fails closed so callers can fall
/// back to raw bytes instead of misrendering.
/// </summary>
public static class Eu16
{
    public sealed class Image
    {
        public int Width;
        public int Height;
        public byte[] Rgba = Array.Empty<byte>();
    }

    private const int MaxDimension = 4096;
    private const long MaxPixels = 1L << 26;

    public static bool HasMagic(byte[] blob)
    {
        return blob.Length >= 3 && blob[0] == '1' && blob[1] == '6';
    }

    public static Image Decode(byte[] blob)
    {
        if (!HasMagic(blob) || blob.Length < 14)
        {
            throw new InvalidDataException("not an EU16 image");
        }
        if (blob[2] != 0)
        {
            throw new InvalidDataException("unsupported EU16 version");
        }
        int width = blob[3] | (blob[4] << 8);
        int height = blob[5] | (blob[6] << 8);
        int entries = blob[7];
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension
            || (long)width * height > MaxPixels || entries <= 0)
        {
            throw new InvalidDataException("implausible EU16 dimensions");
        }
        int pos = 8 + entries * 2;
        if (pos + 4 > blob.Length)
        {
            throw new InvalidDataException("truncated EU16 palette");
        }
        var palette = new byte[entries][];
        for (int i = 0; i < entries; i++)
        {
            int v = blob[8 + i * 2] | (blob[8 + i * 2 + 1] << 8);
            palette[i] = new[]
            {
                (byte)(((v >> 11) & 31) * 255 / 31),
                (byte)(((v >> 5) & 63) * 255 / 63),
                (byte)((v & 31) * 255 / 31),
            };
        }
        long count = ((long)(blob[pos] << 24) | (long)(blob[pos + 1] << 16)
            | (long)(blob[pos + 2] << 8) | blob[pos + 3]);
        pos += 4;
        if (count != (long)width * height)
        {
            throw new InvalidDataException("EU16 pixel count mismatch");
        }
        var img = new Image { Width = width, Height = height };
        img.Rgba = new byte[(long)width * height * 4];
        long at = 0;
        while (pos + 1 < blob.Length && at < (long)width * height)
        {
            int n = blob[pos];
            int index = blob[pos + 1];
            pos += 2;
            if ((uint)index >= (uint)entries || at + n > (long)width * height)
            {
                throw new InvalidDataException("bad EU16 run");
            }
            var color = palette[index];
            for (int k = 0; k < n; k++, at++)
            {
                img.Rgba[at * 4] = color[0];
                img.Rgba[at * 4 + 1] = color[1];
                img.Rgba[at * 4 + 2] = color[2];
                img.Rgba[at * 4 + 3] = 0xFF;
            }
        }
        if (at != (long)width * height || pos != blob.Length)
        {
            throw new InvalidDataException("EU16 pixel count mismatch");
        }
        return img;
    }
}
