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

using Yafex.FileFormats.Lzo;
using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.Ubifs;

/// <summary>A parsed UBIFS inode: identity, size, mode, and symlink data.</summary>
public sealed class UbifsInode
{
    public uint Ino;
    public ulong Size;
    public uint Mode;
    public uint Nlink;
    public uint Mtime;
    public byte[] SymlinkTarget = Array.Empty<byte>();
}

/// <summary>A parsed UBIFS directory entry: parent, name, and child.</summary>
public sealed class UbifsDirent
{
    public uint Parent;
    public uint Ino;
    public string Name = string.Empty;
}

/// <summary>
/// Read-only UBIFS context over a flat volume payload (LEBs concatenated,
/// LEB size known): scans every LEB for CRC-validated nodes and indexes
/// inodes, dentries, and data blocks. Newer journal sequence numbers win
/// on key collisions; deletion dents (child zero) remove entries.
/// </summary>
public sealed class UbifsContext : VfsContext
{
    public int LebSize { get; }

    private readonly Dictionary<uint, UbifsInode> _inos = new();
    private readonly Dictionary<uint, ulong> _inoSeq = new();
    private readonly Dictionary<(uint Parent, string Name), (uint Ino, ulong Seq)> _dents = new();
    private readonly Dictionary<(uint Ino, uint Block), (byte[] Data, uint Compr, uint Size, ulong Seq)> _blocks = new();

    private UbifsContext(int lebSize)
    {
        LebSize = lebSize;
    }

    public static UbifsContext Open(byte[] image, int lebSize)
    {
        if (lebSize < 1024 || lebSize > 1 << 24 || image.Length < 24)
        {
            throw new IOException("bad UBIFS geometry");
        }
        var ctx = new UbifsContext(lebSize);
        long lebs = image.Length / lebSize;
        for (long l = 0; l < lebs; l++)
        {
            ctx.ScanLeb(image, l * lebSize, Math.Min(lebSize, image.Length - l * lebSize));
        }
        if (ctx._inos.Count == 0)
        {
            throw new IOException("no UBIFS inodes found");
        }
        return ctx;
    }

    public static UbifsContext Open(Stream stream)
    {
        if (stream == null || !stream.CanSeek)
        {
            throw new IOException("ubifs needs a seekable stream");
        }
        // LEB size comes from the superblock node opening LEB 0
        byte[] head = new byte[64];
        stream.Position = 0;
        int got = 0;
        while (got < head.Length)
        {
            int n = stream.Read(head, got, head.Length - got);
            if (n <= 0) throw new IOException("image too small for UBIFS superblock");
            got += n;
        }
        if (UbifsConstants.ReadU32(head, 0) != UbifsConstants.Magic
            || UbifsConstants.ReadU32(head, 20) != UbifsConstants.TypeSb)
        {
            throw new IOException("not a UBIFS image");
        }
        int lebSize = (int)UbifsConstants.ReadU32(head, UbifsConstants.SbLebSizeOff);
        byte[] image = new byte[stream.Length];
        stream.Position = 0;
        int off = 0;
        while (off < image.Length)
        {
            int n = stream.Read(image, off, image.Length - off);
            if (n <= 0) throw new IOException("UBIFS short read");
            off += n;
        }
        return Open(image, lebSize);
    }

    private void ScanLeb(byte[] image, long lebStart, long lebLen)
    {
        long pos = lebStart;
        long end = lebStart + lebLen;
        while (pos + UbifsConstants.HeaderSize <= end)
        {
            int at = (int)pos;
            if (UbifsConstants.ReadU32(image, at) != UbifsConstants.Magic) return;
            uint stored = UbifsConstants.ReadU32(image, at + 4);
            ulong sqnum = UbifsConstants.ReadU64(image, at + 8);
            uint len = UbifsConstants.ReadU32(image, at + 16);
            int type = (int)UbifsConstants.ReadU32(image, at + 20);
            if (len < UbifsConstants.HeaderSize || pos + len > end) return;
            if (Support.Crc32.ComputeRaw(image, at + 8, (int)len - 8) != stored) return;
            switch (type)
            {
                case UbifsConstants.TypeIno: ReadIno(image, at, (int)len, sqnum); break;
                case UbifsConstants.TypeData: ReadData(image, at, (int)len, sqnum); break;
                case UbifsConstants.TypeDent: ReadDent(image, at, (int)len, sqnum); break;
                default: break; // index/super/master/padding: positioning only
            }
            pos += (len + 7) & ~7;
        }
    }

    private void ReadIno(byte[] image, int at, int len, ulong sqnum)
    {
        if (len < UbifsConstants.InoFixedSize) return;
        uint keyA = UbifsConstants.ReadU32(image, at + 24);
        uint keyB = UbifsConstants.ReadU32(image, at + 28);
        if ((keyB >> 29) != UbifsConstants.KeyIno) return;
        uint ino = keyA;
        if (_inoSeq.TryGetValue(ino, out ulong old) && old >= sqnum) return;
        var rec = new UbifsInode
        {
            Ino = ino,
            Size = UbifsConstants.ReadU64(image, at + UbifsConstants.InoSizeOff),
            Mode = UbifsConstants.ReadU32(image, at + UbifsConstants.InoModeOff),
            Nlink = UbifsConstants.ReadU32(image, at + UbifsConstants.InoModeOff + 4),
            Mtime = UbifsConstants.ReadU32(image, at + UbifsConstants.InoModeOff - 32),
        };
        if ((rec.Mode & 0xF000) == 0xA000 && len > UbifsConstants.InoFixedSize)
        {
            int tlen = len - UbifsConstants.InoFixedSize;
            rec.SymlinkTarget = new byte[tlen];
            Array.Copy(image, at + UbifsConstants.InoFixedSize, rec.SymlinkTarget, 0, tlen);
        }
        _inos[ino] = rec;
        _inoSeq[ino] = sqnum;
    }

    private void ReadData(byte[] image, int at, int len, ulong sqnum)
    {
        const int fixed_ = UbifsConstants.DataPayloadOff;
        if (len < fixed_) return;
        uint keyA = UbifsConstants.ReadU32(image, at + 24);
        uint keyB = UbifsConstants.ReadU32(image, at + 28);
        if ((keyB >> 29) != UbifsConstants.KeyData) return;
        uint block = keyB & 0x1FFFFFFFu;
        var id = (keyA, block);
        if (_blocks.TryGetValue(id, out var old) && old.Seq >= sqnum) return;
        uint size = UbifsConstants.ReadU32(image, at + UbifsConstants.DataSizeOff);
        uint compr = UbifsConstants.ReadU32(image, at + UbifsConstants.DataComprOff);
        var data = new byte[len - fixed_];
        Array.Copy(image, at + fixed_, data, 0, data.Length);
        _blocks[id] = (data, compr, size, sqnum);
    }

    private void ReadDent(byte[] image, int at, int len, ulong sqnum)
    {
        if (len < UbifsConstants.DentNlenOff + 2) return;
        uint parent = UbifsConstants.ReadU32(image, at + 24);
        uint keyB = UbifsConstants.ReadU32(image, at + 28);
        if ((keyB >> 29) != UbifsConstants.KeyDent) return;
        ulong child = UbifsConstants.ReadU64(image, at + UbifsConstants.DentInoOff);
        int nlen = UbifsConstants.ReadU16(image, at + UbifsConstants.DentNlenOff);
        uint want = keyB & 0x1FFFFFFFu;
        // the fixed header before the name varies between images (4 or 8
        // bytes observed): try each candidate offset, R5 decides. A wrong
        // guess fails the hash ~2^-29, so this stays sound.
        int[] nameCandidates = { 52, 56 };
        int maxLen = len - (UbifsConstants.DentNlenOff + 2);
        if (nlen < 1 || nlen > maxLen || nlen > 255) return;
        int nameAt = -1;
        foreach (int cand in nameCandidates)
        {
            if (cand + nlen > len) continue;
            if ((UbifsConstants.R5Hash(image, at + cand, nlen) & 0x1FFFFFFFu) != want) continue;
            nameAt = at + cand;
            break;
        }
        if (nameAt < 0) return;
        string name;
        try
        {
            name = Encoding.UTF8.GetString(image, nameAt, nlen);
        }
        catch (Exception)
        {
            return;
        }
        if (name.Length == 0 || name.Contains('\0')) return;
        var id = (parent, name);
        if (_dents.TryGetValue(id, out var old) && old.Seq >= sqnum) return;
        if (child == 0)
        {
            _dents.Remove(id);
            return;
        }
        if (child > uint.MaxValue) return;
        _dents[id] = ((uint)child, sqnum);
    }

    public UbifsInode GetInode(uint ino)
    {
        if (_inos.TryGetValue(ino, out var rec)) return rec;
        throw new FileNotFoundException($"UBIFS inode {ino} not found");
    }

    public List<UbifsDirent> ReadDirectory(uint ino)
    {
        var result = new List<UbifsDirent>();
        foreach (var ((parent, name), (child, _)) in _dents)
        {
            if (parent != ino) continue;
            if (name == "." || name == "..") continue;
            result.Add(new UbifsDirent { Parent = parent, Ino = child, Name = name });
        }
        result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    public byte[] ReadFile(uint ino)
    {
        var rec = GetInode(ino);
        if (rec.Size > 1 << 28) throw new IOException("UBIFS file too large");
        var out_ = new byte[rec.Size];
        long done = 0;
        uint block = 0;
        while (done < (long)rec.Size)
        {
            // absent blocks are holes and read as zeros (sparse files)
            byte[] payload = Array.Empty<byte>();
            if (_blocks.TryGetValue((ino, block), out var b))
            {
                payload = Inflate(b.Data, b.Compr, b.Size);
            }
            long want = Math.Min(payload.Length, (long)rec.Size - done);
            if (want > 0)
            {
                Array.Copy(payload, 0, out_, done, want);
                done += want;
            }
            else
            {
                // hole or short final block: zeros to the next block end
                // (or file end), matching UBIFS sparse semantics
                long blockEnd = ((long)block + 1) * UbifsConstants.BlockSize;
                long fillTo = Math.Min(blockEnd, (long)rec.Size);
                while (done < fillTo)
                {
                    out_[done] = 0;
                    done++;
                }
            }
            block++;
            if (block > 1 << 20) throw new IOException("UBIFS block runaway");
        }
        return out_;
    }

    public byte[] ReadSymlink(uint ino)
    {
        var rec = GetInode(ino);
        if (rec.SymlinkTarget.Length > 0) return rec.SymlinkTarget;
        return ReadFile(ino);
    }

    private static byte[] Inflate(byte[] data, uint compr, uint size)
    {
        if (compr == UbifsConstants.ComprNone) return data;
        if (compr == UbifsConstants.ComprLzo)
        {
            // Shared LZO1X decoder (same as LG-LZO payloads): UBIFS stores
            // one raw LZO1X stream per data node, expanding to exactly `size`.
            if (size > 1 << 20) throw new IOException("UBIFS LZO block too large");
            try
            {
                return Lzo1X.Decompress(data, (int)size);
            }
            catch (Exception ex) when (ex is InvalidDataException
                || ex is ArgumentException || ex is ArgumentOutOfRangeException)
            {
                throw new IOException("UBIFS LZO inflate failed", ex);
            }
        }
        if (compr == UbifsConstants.ComprZlib)
        {
            using var raw = new MemoryStream(data, writable: false);
            using var inflater = new DeflateStream(raw, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buf = new byte[1 << 16];
            for (;;)
            {
                int n = inflater.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                output.Write(buf, 0, n);
                if (output.Length > size + (1 << 20)) throw new IOException("UBIFS inflate overrun");
            }
            return output.ToArray();
        }
        throw new IOException($"unsupported UBIFS compression {compr}");
    }
}
