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
using System.Collections.Generic;
using System.IO;

namespace Yafex.FileFormats.Lzo;

/// <summary>
/// LG's LZO framing ("lzopack" in LG/Enyo firmware: lgap.pak and friends).
/// Only the container is LG-specific; the compressed payloads are plain
/// LZO1X v0, decoded by <see cref="Lzo1X"/>.
/// </summary>
/// <remarks>
/// Standard LZO vs LG LZO (documented from observed images + reference behavior):
///
/// - The compression ALGORITHM is identical: LZO1X (method byte 1; level 1 =
///   LZO1X-1, level 9 = LZO1X-999). Any LZO1X v0 decoder handles the blocks;
///   only the framing around them differs.
/// - MAGIC: standard lzop files start with 89 4C 5A 4F 00 0D 0A 1A 0A; LG files
///   start with 00 E9 4C 5A 4F FF 1A 00 ("LZO" embedded at offset 2).
/// - HEADER: lzop carries version, library version, method, level, flags,
///   filter, mode, mtimes, the original filename and a header checksum. LG
///   carries only: u32be decompressed size (advisory — the reference decoder
///   reads but ignores it), u32be flags, u8 method, u8 level, u32be block size.
///   Both use big-endian integers, so endianness is NOT a difference.
/// - METHOD/VERSION quirk: if the method byte is not 1, the reference decoder
///   rewinds one byte and expects a u32be version == 1 followed by method == 1.
///   (A two-word header variant seen on some images.)
/// - BLOCKS: lzop blocks are (uncompressed len, compressed len, uncompressed
///   checksum, compressed checksum?, data). LG blocks are just u32be out_len
///   (0 = end of stream), u32be in_len, data. If in_len &lt; out_len the data is
///   one LZO1X stream that must expand to exactly out_len bytes; if equal, the
///   data is stored raw (incompressible block). in_len &gt; out_len is corrupt.
/// - CHECKSUM: lzop checksums every block plus the header. LG keeps a single
///   trailing u32be Adler-32 over the whole decompressed output, present only
///   when flags &amp; 1, verified after the end marker.
/// - BLOCK SIZE: the reference decoder rejects block_size outside
///   1024..8MiB; typical LG images use 0x40000 (256KiB).
/// </remarks>
public static class LgLzo
{
    public static readonly byte[] Magic = new byte[] { 0x00, 0xE9, 0x4C, 0x5A, 0x4F, 0xFF, 0x1A, 0x00 };

    public const int HeaderSize = 8 + 4 + 4 + 1 + 1 + 4;
    public const uint MinBlockSize = 1024;
    public const uint MaxBlockSize = 8 * 1024 * 1024;

    public static bool HasMagic(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length < Magic.Length) return false;
        for (int i = 0; i < Magic.Length; i++)
        {
            if (prefix[i] != Magic[i]) return false;
        }
        return true;
    }

    public sealed class StreamHeader
    {
        public uint DecompressedSize;
        public uint Flags;
        public byte Method;
        public byte Level;
        public uint BlockSize;
    }

    public sealed class Block
    {
        public long DataOffset;
        public uint CompressedSize;
        public uint UncompressedSize;
        public bool Stored => CompressedSize == UncompressedSize;
    }

    private static uint ReadU32Be(byte[] data, long offset)
    {
        if (offset < 0 || offset + 4 > data.Length)
            throw new InvalidDataException("truncated LG-LZO header");
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
            | ((uint)data[offset + 2] << 8) | data[offset + 3];
    }

    /// <summary>Parses and validates the fixed header; returns it with the
    /// offset of the first block header.</summary>
    public static (StreamHeader Header, long FirstBlock) ReadHeader(byte[] data)
    {
        if (!HasMagic(data))
            throw new InvalidDataException("not an LG-LZO stream");
        var header = new StreamHeader
        {
            DecompressedSize = ReadU32Be(data, 8),
            Flags = ReadU32Be(data, 12),
        };
        long pos = 16;
        byte method = data[pos++];
        if (method != 1)
        {
            // version-word variant: <version:u32be==1><method:u8==1>
            pos -= 1;
            if (ReadU32Be(data, pos) != 1)
                throw new InvalidDataException($"unsupported LG-LZO method {method}");
            pos += 4;
            method = data[pos++];
            if (method != 1)
                throw new InvalidDataException($"unsupported LG-LZO method {method}");
        }
        header.Method = method;
        if (pos + 1 + 4 > data.Length)
            throw new InvalidDataException("truncated LG-LZO header");
        header.Level = data[pos++];
        header.BlockSize = ReadU32Be(data, pos);
        pos += 4;
        if (header.BlockSize < MinBlockSize || header.BlockSize > MaxBlockSize)
            throw new InvalidDataException($"invalid LG-LZO block size {header.BlockSize}");
        return (header, pos);
    }

    /// <summary>Walks the block headers without decompressing anything.
    /// Returns the blocks plus the end offset (start of the checksum trailer,
    /// if any) and the total decompressed length.</summary>
    public static (List<Block> Blocks, long EndOffset, long TotalOut) ReadBlockTable(byte[] data)
    {
        var (header, pos) = ReadHeader(data);
        var blocks = new List<Block>();
        long totalOut = 0;
        while (true)
        {
            uint outLen = ReadU32Be(data, pos);
            pos += 4;
            if (outLen == 0) break; // end marker
            uint inLen = ReadU32Be(data, pos);
            pos += 4;
            if (inLen == 0 || inLen > header.BlockSize || outLen > header.BlockSize || inLen > outLen)
                throw new InvalidDataException("corrupt LG-LZO block header");
            if (pos + inLen > data.Length)
                throw new InvalidDataException("truncated LG-LZO block");
            blocks.Add(new Block { DataOffset = pos, CompressedSize = inLen, UncompressedSize = outLen });
            pos += inLen;
            totalOut += outLen;
        }
        return (blocks, pos, totalOut);
    }

    /// <summary>Decodes one block with the in-house LZO1X decoder;
    /// stored blocks are memcpy'd.</summary>
    public static byte[] DecodeBlock(byte[] data, Block block)
    {
        if (block.Stored)
        {
            var stored = new byte[block.UncompressedSize];
            Array.Copy(data, block.DataOffset, stored, 0, block.UncompressedSize);
            return stored;
        }
        if (block.DataOffset > int.MaxValue - block.CompressedSize || block.CompressedSize > int.MaxValue)
            throw new InvalidDataException("LG-LZO block too large");
        var compressed = new byte[block.CompressedSize];
        Array.Copy(data, block.DataOffset, compressed, 0, block.CompressedSize);
        if (block.UncompressedSize > int.MaxValue)
            throw new InvalidDataException("LG-LZO block too large");
        return Lzo1X.Decompress(compressed, (int)block.UncompressedSize);
    }

    public static uint Adler32(byte[] data, int offset, int count)
    {
        const uint Mod = 65521;
        uint a = 1, b = 0;
        for (int i = 0; i < count; i++)
        {
            a = (a + data[offset + i]) % Mod;
            b = (b + a) % Mod;
        }
        return (b << 16) | a;
    }

    /// <summary>Full decode with trailer checksum verification.
    /// Throws <see cref="InvalidDataException"/> on any corruption.</summary>
    public static byte[] DecompressAll(byte[] data)
    {
        var (header, pos) = ReadHeader(data);
        var (blocks, end, totalOut) = ReadBlockTable(data);
        if (totalOut > int.MaxValue)
            throw new InvalidDataException("LG-LZO output too large");
        var output = new byte[totalOut];
        uint checksum = 1; // adler32("") init
        long outPos = 0;
        foreach (var block in blocks)
        {
            var plain = DecodeBlock(data, block);
            Array.Copy(plain, 0, output, outPos, plain.Length);
            outPos += plain.Length;
            if ((header.Flags & 1) != 0)
            {
                const uint Mod = 65521;
                uint a = checksum & 0xFFFF, b = checksum >> 16;
                foreach (var by in plain)
                {
                    a = (a + by) % Mod;
                    b = (b + a) % Mod;
                }
                checksum = (b << 16) | a;
            }
        }
        if ((header.Flags & 1) != 0)
        {
            uint expected = ReadU32Be(data, end);
            if (expected != checksum)
                throw new InvalidDataException("LG-LZO checksum mismatch");
            end += 4;
        }
        if (end > data.Length)
            throw new InvalidDataException("truncated LG-LZO trailer");
        return output;
    }
}
