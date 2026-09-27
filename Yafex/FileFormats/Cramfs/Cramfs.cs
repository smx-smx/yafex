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
using System.IO.Compression;
using System.Text;

namespace Yafex.FileFormats.Cramfs;

/// <summary>
/// Little-endian cramfs reader, including the LG extensions found in EPK2
/// payloads (e.g. lgap.pak after LZO decompression).
/// </summary>
/// <remarks>
/// On-disk layout (all little-endian): superblock (magic 0x28cd3d45, size,
/// flags, future, "Compressed ROMFS", fsid, name), root inode at offset 64,
/// then inodes and data. Each inode is 12 bytes packed
/// (mode:16, uid:16, size:24, gid:8, namelen:6, offset:26, where offset is a
/// byte offset divided by 4). Directory entries are an inode followed by
/// namelen*4 name bytes (NUL-padded); entries exactly fill the dir size.
/// File data is a table of 4KB-page block pointers followed by the blocks.
///
/// LG extensions (verified byte-exact against real images):
/// - SIZES ABOVE 16MB: the 24-bit size field overflows, so LG stashes the
///   high byte in (gid - root_gid): true_size = size24 + (gid - root_gid)
///   * 0x1000000, applied to regular files when gid exceeds the root gid.
/// - XIP/RAW FILES: mode &amp; S_ISVTX (0x200) marks an uncompressed
///   executable: no block table, payload stored raw starting at the next
///   4096-byte boundary after the data offset, with the (possibly extended)
///   size. Zero "pointers" before it are alignment padding.
/// - BLOCK POINTERS ARE END OFFSETS: each u32 is the absolute file offset of
///   the END of its block (not the start, and with no flag bits, unlike
///   mainline cramfs). Block 0 spans [table_end, ptr0), block i spans
///   [ptr(i-1), ptr(i)). Payloads are zlib streams (header stripped, raw
///   deflate inflated, trailing adler ignored).
/// </remarks>
public sealed class CramfsInode
{
    public ushort Mode;
    public ushort Uid;
    public uint Size24;
    public byte Gid;
    public int NameLengthUnits;
    public uint DataOffset;
    public string Name = "";

    public bool IsDirectory => (Mode & 0xF000) == 0x4000;
    public bool IsRegular => (Mode & 0xF000) == 0x8000;
    public bool IsSymlink => (Mode & 0xF000) == 0xA000;
    public bool IsRaw => (Mode & 0x200) != 0;
}

public sealed class CramfsReader
{
    public const uint Magic = 0x28CD3D45;
    public const int BlockSize = 4096;
    public const int RootOffset = 64;

    private readonly byte[] _data;

    public uint DeclaredSize { get; }
    public uint Flags { get; }
    public string VolumeName { get; }
    public byte RootGid { get; }
    public CramfsInode Root { get; }

    public CramfsReader(byte[] data)
    {
        _data = data;
        if (data.Length < 76)
            throw new InvalidDataException("too small for cramfs");
        if (ReadU32(0) != Magic)
            throw new InvalidDataException("not a cramfs image");
        DeclaredSize = ReadU32(4);
        Flags = ReadU32(8);
        var sig = Encoding.ASCII.GetString(data, 16, 16);
        if (sig != "Compressed ROMFS")
            throw new InvalidDataException("bad cramfs signature");
        VolumeName = Encoding.ASCII.GetString(data, 48, 16).TrimEnd('\0');
        Root = ReadInode(RootOffset, "");
        RootGid = Root.Gid;
    }

    private uint ReadU32(long offset)
    {
        if (offset < 0 || offset + 4 > _data.Length)
            throw new InvalidDataException("cramfs read out of range");
        return BitConverter.ToUInt32(_data, (int)offset);
    }

    private CramfsInode ReadInode(long offset, string name)
    {
        if (offset < 0 || offset + 12 > _data.Length)
            throw new InvalidDataException("cramfs inode out of range");
        uint w0 = ReadU32(offset);
        uint w1 = ReadU32(offset + 4);
        uint w2 = ReadU32(offset + 8);
        return new CramfsInode
        {
            Mode = (ushort)(w0 & 0xFFFF),
            Uid = (ushort)(w0 >> 16),
            Size24 = w1 & 0xFFFFFF,
            Gid = (byte)(w1 >> 24),
            NameLengthUnits = (int)(w2 & 0x3F),
            DataOffset = (w2 >> 6) << 2,
            Name = name,
        };
    }

    /// <summary>LG size extension for regular files (see class remarks).</summary>
    public static uint ExtendSize(uint size24, byte gid, byte rootGid)
    {
        if (gid > rootGid)
            return size24 + (uint)(gid - rootGid) * 0x1000000u;
        return size24;
    }

    public uint FileSize(CramfsInode inode)
    {
        if (inode.IsRegular)
            return ExtendSize(inode.Size24, inode.Gid, RootGid);
        return inode.Size24;
    }

    public List<CramfsInode> ReadDirectory(CramfsInode dir)
    {
        if (!dir.IsDirectory)
            throw new InvalidDataException("not a directory");
        var entries = new List<CramfsInode>();
        long pos = dir.DataOffset;
        long end = pos + dir.Size24;
        if (end > _data.Length)
            throw new InvalidDataException("cramfs directory out of range");
        while (pos < end)
        {
            var inode = ReadInode(pos, "");
            int nameBytes = inode.NameLengthUnits * 4;
            if (nameBytes <= 0 || pos + 12 + nameBytes > end)
                throw new InvalidDataException("bad cramfs dir entry");
            int len = nameBytes;
            while (len > 0 && _data[pos + 12 + len - 1] == 0) len--;
            inode.Name = Encoding.ASCII.GetString(_data, (int)(pos + 12), len);
            entries.Add(inode);
            pos += 12 + nameBytes;
        }
        if (pos != end)
            throw new InvalidDataException("cramfs directory size mismatch");
        return entries;
    }

    private byte[] InflateBlock(byte[] compressed, int expected)
    {
        // zlib header (0x78..) stripped; trailing adler32 left unread
        int start = compressed.Length >= 2 && compressed[0] == 0x78 ? 2 : 0;
        using var raw = new MemoryStream(compressed, start, compressed.Length - start, writable: false);
        using var inflater = new DeflateStream(raw, CompressionMode.Decompress);
        var output = new byte[expected];
        int off = 0;
        while (off < expected)
        {
            int n = inflater.Read(output, off, expected - off);
            if (n <= 0) break;
            off += n;
        }
        if (off != expected)
            throw new InvalidDataException("cramfs block did not inflate to its page size");
        return output;
    }

    private byte[] ReadBlocks(long tableOffset, uint size)
    {
        int pages = (int)((size + BlockSize - 1) / BlockSize);
        if (pages == 0) return Array.Empty<byte>();
        long tableEnd = tableOffset + 4L * pages;
        if (tableEnd > _data.Length)
            throw new InvalidDataException("cramfs block table out of range");
        var output = new byte[size];
        long prevEnd = tableEnd;
        for (int i = 0; i < pages; i++)
        {
            long blockEnd = ReadU32(tableOffset + 4L * i);
            if (blockEnd < prevEnd || blockEnd > _data.Length)
                throw new InvalidDataException("bad cramfs block pointer");
            int pageLen = (int)Math.Min(BlockSize, size - (long)i * BlockSize);
            int stored = (int)(blockEnd - prevEnd);
            var compressed = new byte[stored];
            Array.Copy(_data, prevEnd, compressed, 0, stored);
            var page = InflateBlock(compressed, pageLen);
            Array.Copy(page, 0, output, i * BlockSize, pageLen);
            prevEnd = blockEnd;
        }
        return output;
    }

    /// <summary>Full content of a regular file (raw page-aligned or blocked).</summary>
    public byte[] ReadFile(CramfsInode file)
    {
        if (!file.IsRegular)
            throw new InvalidDataException("not a regular file");
        uint size = FileSize(file);
        if (size == 0) return Array.Empty<byte>();
        if (file.IsRaw)
        {
            long start = (file.DataOffset + BlockSize - 1L) & ~(BlockSize - 1L);
            if (start + size > _data.Length)
                throw new InvalidDataException("cramfs raw file out of range");
            var raw = new byte[size];
            Array.Copy(_data, start, raw, 0, size);
            return raw;
        }
        return ReadBlocks(file.DataOffset, size);
    }

    /// <summary>Symlink target (single small block, no NUL terminator).</summary>
    public string ReadSymlink(CramfsInode link)
    {
        if (!link.IsSymlink)
            throw new InvalidDataException("not a symlink");
        if (link.Size24 == 0) return "";
        var bytes = ReadBlocks(link.DataOffset, link.Size24);
        int len = bytes.Length;
        while (len > 0 && bytes[len - 1] == 0) len--;
        return Encoding.ASCII.GetString(bytes, 0, len);
    }
}
