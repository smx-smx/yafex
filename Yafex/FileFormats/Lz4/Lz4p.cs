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

namespace Yafex.FileFormats.Lz4;

/// <summary>
/// LG LZ4P archive: magic, header words, a table of per-block compressed
/// sizes, then raw LZ4 blocks. Every block but the last decompresses to
/// exactly <c>ChunkSize</c> bytes; trailing bytes past the blocks (e.g. the
/// 5376-byte signature tail on some kernels) are ignored.
/// </summary>
public static class Lz4p
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("LZ4P");
    public const int HeaderSize = 0x20;
    public const int MaxChunkSize = 16 << 20;
    public const int MaxBlocks = 100000;

    public sealed class Archive
    {
        public int ChunkSize;
        public int BlockCount;
        public uint[] Sizes = Array.Empty<uint>();
        public int PayloadOffset;
    }

    public static bool HasMagic(byte[] prefix)
    {
        if (prefix.Length < Magic.Length)
        {
            return false;
        }
        for (int i = 0; i < Magic.Length; i++)
        {
            if (prefix[i] != Magic[i])
            {
                return false;
            }
        }
        return true;
    }

    private static uint ReadU32LE(byte[] buf, int off)
    {
        return (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));
    }

    public static bool TryReadArchive(byte[] image, out Archive archive)
    {
        archive = new Archive();
        try
        {
            if (image.Length < HeaderSize || !HasMagic(image))
            {
                return false;
            }
            int chunk = (int)ReadU32LE(image, 12);
            long count = ReadU32LE(image, 16);
            if (chunk < 1 || chunk > MaxChunkSize || count < 1 || count > MaxBlocks)
            {
                return false;
            }
            long tableEnd = (long)HeaderSize + count * 4;
            if (tableEnd > image.Length)
            {
                return false;
            }
            var sizes = new uint[count];
            long sum = 0;
            for (long i = 0; i < count; i++)
            {
                sizes[i] = ReadU32LE(image, HeaderSize + (int)(i * 4));
                if (sizes[i] < 1)
                {
                    return false;
                }
                sum += sizes[i];
            }
            if (tableEnd + sum > image.Length)
            {
                return false;
            }
            archive.ChunkSize = chunk;
            archive.BlockCount = (int)count;
            archive.Sizes = sizes;
            archive.PayloadOffset = (int)tableEnd;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static byte[] Decode(byte[] image)
    {
        if (!TryReadArchive(image, out var archive))
        {
            throw new InvalidDataException("not an LZ4P archive");
        }
        // (count-1) full chunks + a final partial chunk, at most
        long capacity = (long)(archive.BlockCount - 1) * archive.ChunkSize + archive.ChunkSize;
        if (capacity > int.MaxValue)
        {
            throw new InvalidDataException("LZ4P output too large");
        }
        var dst = new byte[capacity];
        int op = 0;
        int ip = archive.PayloadOffset;
        for (int n = 0; n < archive.BlockCount; n++)
        {
            int srcLen = (int)archive.Sizes[n];
            bool last = n == archive.BlockCount - 1;
            int produced = Lz4.DecompressBlock(image, ip, srcLen, dst, op, archive.ChunkSize, out int consumed);
            if (!last && (produced != archive.ChunkSize || consumed != srcLen))
            {
                throw new InvalidDataException("LZ4P block size mismatch");
            }
            ip += srcLen;
            op += produced;
        }
        if (op < capacity)
        {
            Array.Resize(ref dst, op);
        }
        return dst;
    }
}
