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
using System.IO.Compression;

using Yafex.Support;

namespace Yafex.FileFormats.Idb;

/// <summary>
/// Minimal RGBA PNG encoder (8-bit, non-interlaced, filter 0): signature,
/// IHDR, single IDAT (zlib), IEND, with IEEE CRCs. Enough to materialize
/// decoded framebuffer images as strictly valid PNG files.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature =
    {
        137, 80, 78, 71, 13, 10, 26, 10,
    };

    public static byte[] EncodeRgba(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0 || rgba.Length != (long)width * height * 4)
        {
            throw new ArgumentException("bad RGBA dimensions");
        }
        using var out_ = new MemoryStream();
        out_.Write(Signature, 0, Signature.Length);
        var ihdr = new byte[13];
        WriteU32BE(ihdr, 0, width);
        WriteU32BE(ihdr, 4, height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // color type: truecolor+alpha
        WriteChunk(out_, "IHDR", ihdr);

        using var raw = new MemoryStream();
        var stride = width * 4;
        var zero = new byte[1];
        for (int y = 0; y < height; y++)
        {
            raw.Write(zero, 0, 1);
            raw.Write(rgba, y * stride, stride);
        }
        raw.Position = 0;
        using var deflated = new MemoryStream();
        using (var zs = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.CopyTo(zs);
        }
        WriteChunk(out_, "IDAT", deflated.ToArray());
        WriteChunk(out_, "IEND", Array.Empty<byte>());
        return out_.ToArray();
    }

    private static void WriteU32BE(byte[] buf, int off, int value)
    {
        buf[off] = (byte)(value >> 24);
        buf[off + 1] = (byte)(value >> 16);
        buf[off + 2] = (byte)(value >> 8);
        buf[off + 3] = (byte)value;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var typeBytes = new[]
        {
            (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3],
        };
        var len = new byte[4];
        WriteU32BE(len, 0, data.Length);
        s.Write(len, 0, 4);
        s.Write(typeBytes, 0, 4);
        s.Write(data, 0, data.Length);
        var crcBuf = new byte[4 + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcBuf, 0, 4);
        Buffer.BlockCopy(data, 0, crcBuf, 4, data.Length);
        var crc = new byte[4];
        WriteU32BE(crc, 0, unchecked((int)Crc32.Compute(crcBuf, 0, crcBuf.Length)));
        s.Write(crc, 0, 4);
    }
}
