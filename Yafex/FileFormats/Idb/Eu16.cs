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
/// LG "16" image decoder (IDB blobs). Big-endian header: '16', width,
/// height,
/// palette entry count (all u16), palette of RGB565 big-endian entries,
/// big-endian pixel count, then a command stream. First byte of each
/// command selects by top two bits: 0x00 = run (count6 + 1 index byte,
/// palette pixel repeated), 0x40 = literals (count6 index bytes),
/// 0x80 = history back-reference (count6 pixels copied from the 256-byte
/// recent-index ring, then palette-resolved), 0xC0 = single palette pixel
/// with 13-bit index when bit5 clear, else count5 raw RGB565BE pixels.
/// Decoding stops when the emitted count reaches the header total; anything
/// truncated, out of range or overrunning fails closed.
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
        if (!HasMagic(blob) || blob.Length < 12)
        {
            throw new InvalidDataException("not an EU16 image");
        }
        int width = (blob[2] << 8) | blob[3];
        int height = (blob[4] << 8) | blob[5];
        int entries = (blob[6] << 8) | blob[7];
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension
            || (long)width * height > MaxPixels || entries < 0)
        {
            throw new InvalidDataException("implausible EU16 dimensions");
        }
        long palEnd = 8L + (long)entries * 2;
        if (palEnd + 4 > blob.Length)
        {
            throw new InvalidDataException("truncated EU16 palette");
        }
        long total = ((long)blob[palEnd] << 24) | ((long)blob[palEnd + 1] << 16)
            | ((long)blob[palEnd + 2] << 8) | blob[palEnd + 3];
        long want = (long)width * height;
        if (total != want)
        {
            throw new InvalidDataException("EU16 pixel count mismatch");
        }
        var palette = new byte[entries][];
        for (int i = 0; i < entries; i++)
        {
            int v = (blob[8 + i * 2] << 8) | blob[8 + i * 2 + 1];
            palette[i] = new[]
            {
                (byte)(((v >> 11) & 31) * 255 / 31),
                (byte)(((v >> 5) & 63) * 255 / 63),
                (byte)((v & 31) * 255 / 31),
            };
        }
        var img = new Image { Width = width, Height = height };
        img.Rgba = new byte[want * 4];
        var history = new byte[256];
        int cursor = 0;
        long at = 0;
        long written = 0;
        long p = palEnd + 4;
        var backref = new byte[64];
        while (at < total)
        {
            if (p >= blob.Length)
            {
                throw new InvalidDataException("truncated EU16 stream");
            }
            int b = blob[p];
            switch (b & 0xC0)
            {
                case 0x40: // literals: count6 palette indices
                {
                    int k = b & 0x3F;
                    p++;
                    at += k;
                    for (int j = 0; j < k; j++)
                    {
                        if (p >= blob.Length)
                        {
                            throw new InvalidDataException("truncated EU16 stream");
                        }
                        int index = blob[p++];
                        WritePixel(img.Rgba, ref written, want, Resolve(palette, index));
                        history[cursor++ & 0xFF] = (byte)index;
                    }
                    break;
                }
                case 0x80: // history back-reference, then palette resolve
                {
                    int k = b & 0x3F;
                    if (p + 1 >= blob.Length)
                    {
                        throw new InvalidDataException("truncated EU16 stream");
                    }
                    int start = blob[p + 1];
                    p += 2;
                    at += k;
                    for (int j = 0; j < k; j++)
                    {
                        backref[j] = history[(start + j) & 0xFF];
                    }
                    for (int j = 0; j < k; j++)
                    {
                        history[cursor++ & 0xFF] = backref[j];
                        WritePixel(img.Rgba, ref written, want, Resolve(palette, backref[j]));
                    }
                    break;
                }
                case 0xC0:
                {
                    if ((b & 0x20) == 0) // single palette pixel, 13-bit index
                    {
                        if (p + 1 >= blob.Length)
                        {
                            throw new InvalidDataException("truncated EU16 stream");
                        }
                        int index = ((b & 0x1F) << 8) | blob[p + 1];
                        p += 2;
                        at++;
                        WritePixel(img.Rgba, ref written, want, Resolve(palette, index));
                    }
                    else // raw RGB565BE pixels
                    {
                        int k = b & 0x1F;
                        p++;
                        at += k;
                        for (int j = 0; j < k; j++)
                        {
                            if (p + 1 >= blob.Length)
                            {
                                throw new InvalidDataException("truncated EU16 stream");
                            }
                            int v = (blob[p] << 8) | blob[p + 1];
                            p += 2;
                            WritePixel(img.Rgba, ref written, want, Expand565(v));
                        }
                    }
                    break;
                }
                default: // 0x00: run of one palette pixel
                {
                    int k = b & 0x3F;
                    if (p + 1 >= blob.Length)
                    {
                        throw new InvalidDataException("truncated EU16 stream");
                    }
                    int index = blob[p + 1];
                    p += 2;
                    at += k;
                    var color = Resolve(palette, index);
                    for (int j = 0; j < k; j++)
                    {
                        WritePixel(img.Rgba, ref written, want, color);
                    }
                    break;
                }
            }
        }
        if (at != total || written != want)
        {
            throw new InvalidDataException("EU16 pixel count mismatch");
        }
        return img;
    }

    private static byte[] Resolve(byte[][] palette, int index)
    {
        if ((uint)index >= (uint)palette.Length)
        {
            throw new InvalidDataException("bad EU16 palette index");
        }
        return palette[index];
    }

    private static byte[] Expand565(int v)
    {
        return new[]
        {
            (byte)(((v >> 11) & 31) * 255 / 31),
            (byte)(((v >> 5) & 63) * 255 / 63),
            (byte)((v & 31) * 255 / 31),
        };
    }

    private static void WritePixel(byte[] rgba, ref long written, long want, byte[] color)
    {
        if (written >= want)
        {
            throw new InvalidDataException("EU16 pixel count mismatch");
        }
        rgba[written * 4] = color[0];
        rgba[written * 4 + 1] = color[1];
        rgba[written * 4 + 2] = color[2];
        rgba[written * 4 + 3] = 0xFF;
        written++;
    }
}
