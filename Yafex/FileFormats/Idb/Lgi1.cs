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
/// LG LGI1 image decoder (IDB blobs). Big-endian header: magic, version,
/// palette depth, pixel depth, format flags, dimensions; optional palette
/// (count + entry size + byte size); pixel data size + pixels, optionally
/// RLE-compressed with a data-driven marker byte and 15-bit extended
/// counts. Indexed pixels map through the palette; direct pixels are raw.
/// Palette entries and direct-32 pixels are alpha-first (ARGB); the alpha
/// position is proven by palette statistics (byte0 bimodal ff/00) and the
/// RGB order is supported 3:1 by saturated red-vs-blue counts across the
/// reference corpus. Fails closed on any anomaly.
/// </summary>
public static class Lgi1
{
    public sealed class Image
    {
        public int Width;
        public int Height;
        public byte[] Rgba = Array.Empty<byte>();
    }

    private const int MaxDimension = 4096;
    private const long MaxPixels = 1L << 26;

    private static int ReadU16BE(byte[] b, int o) => (b[o] << 8) | b[o + 1];

    private static int ReadU32BE(byte[] b, int o) =>
        (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];

    public static bool HasMagic(byte[] blob)
    {
        return blob.Length >= 4 && blob[0] == 'L' && blob[1] == 'G'
            && blob[2] == 'I' && blob[3] == '1';
    }

    public static Image Decode(byte[] blob)
    {
        if (!HasMagic(blob) || blob.Length < 12)
        {
            throw new InvalidDataException("not an LGI1 image");
        }
        int paletteBpp = blob[5];
        int imageBpp = blob[6];
        int format = blob[7];
        int width = ReadU16BE(blob, 8);
        int height = ReadU16BE(blob, 10);
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension
            || (long)width * height > MaxPixels)
        {
            throw new InvalidDataException("implausible LGI1 dimensions");
        }
        if (imageBpp != 8 && imageBpp != 16 && imageBpp != 24 && imageBpp != 32)
        {
            throw new InvalidDataException("unsupported LGI1 pixel depth");
        }
        bool indexed = (format & 0x80) != 0;
        bool rle = (format & 0x40) != 0;
        int pos = 12;

        byte[][]? palette = null;
        if (indexed)
        {
            if (pos + 8 > blob.Length)
            {
                throw new InvalidDataException("truncated LGI1 palette header");
            }
            int entries = ReadU16BE(blob, pos);
            int entrySize = blob[pos + 2];
            pos += 4; // +1 pad byte
            int paletteBytes = ReadU32BE(blob, pos);
            pos += 4;
            if (entries <= 0 || entries > 65536 || entrySize < 1 || entrySize > 4
                || paletteBytes < entries * entrySize
                || pos + paletteBytes > blob.Length)
            {
                throw new InvalidDataException("bad LGI1 palette");
            }
            if (paletteBpp != entrySize * 8)
            {
                throw new InvalidDataException("LGI1 palette depth mismatch");
            }
            palette = new byte[entries][];
            for (int i = 0; i < entries; i++)
            {
                var entry = new byte[entrySize];
                Buffer.BlockCopy(blob, pos + i * entrySize, entry, 0, entrySize);
                palette[i] = entry;
            }
            pos += paletteBytes;
        }

        if (pos + 4 > blob.Length)
        {
            throw new InvalidDataException("truncated LGI1 pixel size");
        }
        int pixelBytes = ReadU32BE(blob, pos);
        pos += 4;
        if (pixelBytes < 0 || pos + pixelBytes > blob.Length)
        {
            throw new InvalidDataException("truncated LGI1 pixels");
        }
        var pixelData = new byte[pixelBytes];
        Buffer.BlockCopy(blob, pos, pixelData, 0, pixelBytes);

        int valueSize = imageBpp / 8;
        byte[] pixels = rle ? DecodeRle(pixelData, valueSize) : pixelData;

        var img = new Image { Width = width, Height = height };
        if (indexed)
        {
            if (pixels.Length != (long)width * height * valueSize)
            {
                throw new InvalidDataException("LGI1 pixel count mismatch");
            }
            img.Rgba = new byte[(long)width * height * 4];
            for (long p = 0, o = 0; p < (long)width * height; p++, o += 4)
            {
                int index = valueSize == 1
                    ? pixels[p]
                    : (pixels[p * 2] << 8) | pixels[p * 2 + 1];
                if ((uint)index >= (uint)palette!.Length)
                {
                    throw new InvalidDataException("LGI1 palette index out of range");
                }
                WriteEntry(img.Rgba, (int)o, palette[index]);
            }
        }
        else
        {
            if (pixels.Length != (long)width * height * valueSize)
            {
                throw new InvalidDataException("LGI1 pixel count mismatch");
            }
            if (valueSize == 4)
            {
                // alpha-first, like the palette entries
                img.Rgba = new byte[pixels.Length];
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    img.Rgba[i] = pixels[i + 1];
                    img.Rgba[i + 1] = pixels[i + 2];
                    img.Rgba[i + 2] = pixels[i + 3];
                    img.Rgba[i + 3] = pixels[i];
                }
            }
            else if (valueSize == 3)
            {
                img.Rgba = new byte[(long)width * height * 4];
                for (long p = 0, o = 0, s = 0; p < (long)width * height; p++, o += 4, s += 3)
                {
                    img.Rgba[o] = pixels[s];
                    img.Rgba[o + 1] = pixels[s + 1];
                    img.Rgba[o + 2] = pixels[s + 2];
                    img.Rgba[o + 3] = 0xFF;
                }
            }
            else
            {
                throw new InvalidDataException("unsupported LGI1 direct depth");
            }
        }
        return img;
    }

    private static void WriteEntry(byte[] rgba, int off, byte[] entry)
    {
        if (entry.Length == 4)
        {
            rgba[off] = entry[1];
            rgba[off + 1] = entry[2];
            rgba[off + 2] = entry[3];
            rgba[off + 3] = entry[0];
        }
        else if (entry.Length == 3)
        {
            rgba[off] = entry[0];
            rgba[off + 1] = entry[1];
            rgba[off + 2] = entry[2];
            rgba[off + 3] = 0xFF;
        }
        else
        {
            throw new InvalidDataException("unsupported LGI1 palette entry size");
        }
    }

    private static byte[] DecodeRle(byte[] data, int valueSize)
    {
        if (data.Length == 0)
        {
            throw new InvalidDataException("empty LGI1 RLE stream");
        }
        byte marker = data[0];
        var out_ = new System.Collections.Generic.List<byte>(data.Length * 2);
        int i = 1;
        while (i < data.Length)
        {
            if (data[i] == marker)
            {
                i++;
                if (i >= data.Length)
                {
                    throw new InvalidDataException("truncated LGI1 run");
                }
                int count = data[i++];
                if ((count & 0x80) != 0)
                {
                    if (i >= data.Length)
                    {
                        throw new InvalidDataException("truncated LGI1 extended run");
                    }
                    count = ((count & 0x7F) << 8) | data[i++];
                }
                if (i + valueSize > data.Length || count + 1 > MaxPixels)
                {
                    throw new InvalidDataException("truncated LGI1 run value");
                }
                for (int k = 0; k <= count; k++)
                {
                    for (int b = 0; b < valueSize; b++)
                    {
                        out_.Add(data[i + b]);
                    }
                }
                i += valueSize;
            }
            else
            {
                if (i + valueSize > data.Length)
                {
                    throw new InvalidDataException("truncated LGI1 literal");
                }
                for (int b = 0; b < valueSize; b++)
                {
                    out_.Add(data[i + b]);
                }
                i += valueSize;
            }
        }
        return out_.ToArray();
    }
}
