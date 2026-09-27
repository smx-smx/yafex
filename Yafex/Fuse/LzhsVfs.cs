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

using Smx.SharpIO.Memory.Buffers;

using Yafex.FileFormats.Lzhs;
using Yafex.FileFormats.LzhsFs;

namespace Yafex.Fuse;

/// <summary>LZHS-FS container as a virtual file: 1MB raw heading plus
/// chunk outputs tiled contiguously. Chunks decode independently on demand
/// (RAM stays O(largest chunk)); backward seeks restart from chunk zero.
/// Never materializes the full blob.</summary>
public sealed class LzhsFsVirtualFile : IVirtualFile
{
    public const long HeadingSize = 0x100000;

    private readonly IVirtualFile _source;

    public LzhsFsVirtualFile(string name, IVirtualFile source)
    {
        Name = name;
        _source = source;
    }

    public string Name { get; }

    public long Length => WalkTable(_source).Total;

    public Stream OpenRead() => new LzhsFsDecodeStream(_source, WalkTable(_source));

    public sealed class Chunk
    {
        public long InOff;
        public long SegU;
        public long SegC;
        public long InU;
        public bool ZeroFill;
        public bool Uncompressed;
    }

    public sealed class Table
    {
        public readonly List<Chunk> Chunks = new();
        public long Total = HeadingSize;
    }

    internal static Table WalkTable(IVirtualFile file)
    {
        var table = new Table();
        using var s = file.OpenRead();
        if (s.Length < HeadingSize + 32) throw new InvalidDataException("truncated lzhs-fs");
        long inOff = HeadingSize;
        int guard = 0;
        var hdr = new byte[32];
        while (inOff + 32 <= s.Length)
        {
            s.Seek(inOff, SeekOrigin.Begin);
            Fill(s, hdr);
            if (!SpareZero(hdr, 10, 6) || !SpareZero(hdr, 26, 6)) throw new InvalidDataException("bad lzhs chunk header");
            long segU = BitConverter.ToUInt32(hdr, 0);
            long segC = BitConverter.ToUInt32(hdr, 4);
            long inU = BitConverter.ToUInt32(hdr, 16);
            long inC = BitConverter.ToUInt32(hdr, 20);
            ushort inSum = BitConverter.ToUInt16(hdr, 24);
            if (segU <= 0 || segU > (1L << 31) || inU < 0 || inU > (1L << 31)) throw new InvalidDataException("bad lzhs chunk sizes");
            long segSize = 16 + Pad(segC);
            if (inOff + segSize > s.Length) throw new InvalidDataException("truncated lzhs chunk");
            table.Chunks.Add(new Chunk
            {
                InOff = inOff,
                SegU = segU,
                SegC = segC,
                InU = inU,
                ZeroFill = segC == 0 && segU > 0,
                Uncompressed = inC == inU && inSum == 0,
            });
            table.Total += segU;
            inOff += segSize;
            if (++guard > 1000000) throw new InvalidDataException("lzhs chunk runaway");
        }
        if (table.Chunks.Count == 0) throw new InvalidDataException("no lzhs chunks");
        return table;
    }

    internal static long Pad(long n) => n + ((16 - (n % 16)) % 16);

    private static bool SpareZero(byte[] buf, int off, int len)
    {
        for (int i = 0; i < len; i++) if (buf[off + i] != 0) return false;
        return true;
    }

    private static void Fill(Stream s, byte[] buf)
    {
        int off = 0;
        while (off < buf.Length)
        {
            int n = s.Read(buf, off, buf.Length - off);
            if (n <= 0) throw new InvalidDataException("truncated lzhs-fs");
            off += n;
        }
    }
}

/// <summary>Forward decode stream over LZHS-FS chunks: heading passthrough,
/// per-chunk decode (uncompressed passthrough, zero fill, or LZHS decode),
/// restart on backward seek. RAM stays O(largest chunk).</summary>
public sealed class LzhsFsDecodeStream : Stream
{
    private readonly IVirtualFile _file;
    private readonly LzhsFsVirtualFile.Table _table;
    private Stream _src;
    private long _position;
    private int _idx = -1;
    private byte[] _chunkBuf = [];
    private int _chunkPos;

    public LzhsFsDecodeStream(IVirtualFile file, LzhsFsVirtualFile.Table table)
    {
        _file = file;
        _table = table;
        _src = file.OpenRead();
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _table.Total;
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _table.Total) return 0;
        count = (int)Math.Min(count, _table.Total - _position);
        int total = 0;
        while (total < count)
        {
            if (_position < LzhsFsVirtualFile.HeadingSize)
            {
                int take = (int)Math.Min(count - total, LzhsFsVirtualFile.HeadingSize - _position);
                _src.Seek(_position, SeekOrigin.Begin);
                int n = Fill(_src, buffer, offset + total, take);
                if (n <= 0) break;
                total += n;
                _position += n;
                continue;
            }
            if (!EnsureChunk(_position)) break;
            int avail = _chunkBuf.Length - _chunkPos;
            int want = Math.Min(avail, count - total);
            Array.Copy(_chunkBuf, _chunkPos, buffer, offset + total, want);
            _chunkPos += want;
            _position += want;
            total += want;
        }
        return total;
    }

    private bool EnsureChunk(long outPos)
    {
        long rel = outPos - LzhsFsVirtualFile.HeadingSize;
        int i = _idx >= 0 ? _idx : 0;
        if (rel < ChunkStart(i))
        {
            i = 0;
            _idx = -1;
        }
        while (i < _table.Chunks.Count && rel >= ChunkStart(i) + _table.Chunks[i].SegU) i++;
        if (i >= _table.Chunks.Count) return false;
        if (i != _idx)
        {
            DecodeChunk(i);
            _idx = i;
        }
        _chunkPos = (int)(rel - ChunkStart(i));
        return _chunkPos < _chunkBuf.Length;
    }

    private long ChunkStart(int i)
    {
        long off = 0;
        for (int k = 0; k < i; k++) off += _table.Chunks[k].SegU;
        return off;
    }

    private void DecodeChunk(int i)
    {
        var c = _table.Chunks[i];
        if (c.ZeroFill || c.SegU == 0)
        {
            _chunkBuf = new byte[c.SegU];
            return;
        }
        _src.Seek(c.InOff, SeekOrigin.Begin);
        if (c.Uncompressed)
        {
            // raw payload follows the 32 bytes of chunk headers
            _src.Seek(c.InOff + 32, SeekOrigin.Begin);
            var raw = new byte[c.SegU];
            Fill(_src, raw, 0, raw.Length);
            _chunkBuf = raw;
            return;
        }
        // compressed region (inner header + payload), mirroring SegmentData
        var comp = new byte[LzhsFsVirtualFile.Pad(c.SegC)];
        _src.Seek(c.InOff + 16, SeekOrigin.Begin);
        Fill(_src, comp, 0, comp.Length);
        _chunkBuf = new LzhsDecoder(new Memory64<byte>(comp)).DecodeToArray();
    }

    private static int Fill(Stream s, byte[] buf, int off, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = s.Read(buf, off + total, count - total);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _table.Total + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (target < 0 || target > _table.Total) throw new IOException("seek out of range");
        if (target < _position)
        {
            // restart: drop decoded state, reopen source
            _src.Dispose();
            _src = _file.OpenRead();
            _idx = -1;
            _chunkBuf = [];
            _chunkPos = 0;
        }
        _position = target;
        return target;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _src.Dispose();
        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>LZHS-FS container expander: cheap header-only match at mount;
/// the lazy directory decodes chunks on first access (never pre-materialized)
/// and chains squashfs/gzip for the inflated blob.</summary>
public sealed class LzhsFsExpander : IContentExpander
{
    public string Name => "lzhsfs";

    public bool Matches(IVirtualFile file)
    {
        try
        {
            using var s = file.OpenRead();
            if (s.Length < LzhsFsVirtualFile.HeadingSize + 32) return false;
            s.Seek(LzhsFsVirtualFile.HeadingSize, SeekOrigin.Begin);
            var hdr = new byte[32];
            int off = 0;
            while (off < hdr.Length)
            {
                int n = s.Read(hdr, off, hdr.Length - off);
                if (n <= 0) return false;
                off += n;
            }
            return FirstChunkSane(hdr, s.Length);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool FirstChunkSane(byte[] hdr, long total)
    {
        for (int i = 10; i < 16; i++) if (hdr[i] != 0) return false;
        for (int i = 26; i < 32; i++) if (hdr[i] != 0) return false;
        long segU = BitConverter.ToUInt32(hdr, 0);
        long inU = BitConverter.ToUInt32(hdr, 16);
        return segU > 0 && segU <= (1L << 31) && inU >= 0 && inU <= (1L << 31)
            && LzhsFsVirtualFile.HeadingSize + 16 + inU <= total + (1L << 20);
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            var inflated = new LzhsFsVirtualFile(file.Name + ".bin", file);
            // chain container readers for the inflated blob (never itself)
            if (TryOpenSquashfs(inflated, keepAlive, out var tree))
            {
                return tree;
            }
            return new IVfsNode[] { new PakVfsFile(inflated, Helpers.OctalLiteral(0444)) };
        });
    }

    private static bool TryOpenSquashfs(IVirtualFile inflated, ICollection<object> keepAlive, out List<IVfsNode> tree)
    {
        tree = new List<IVfsNode>();
        try
        {
            if (!VfsSquashfs.TryOpen(inflated, keepAlive, out var reader)) return false;
            var tmp = new YafexDirectory(inflated.Name, Helpers.OctalLiteral(0755));
            VfsSquashfs.BuildTree(tmp, reader);
            foreach (var kv in tmp.Tree) tree.Add(kv.Value.Node);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>Single-stream LZHS expander: header sanity at mount, full inflate
/// of the (typically small) stream on first access, then squashfs/gzip chain.</summary>
public sealed class LzhsExpander : IContentExpander
{
    public string Name => "lzhs";

    public bool Matches(IVirtualFile file)
    {
        try
        {
            using var s = file.OpenRead();
            if (s.Length < 16) return false;
            var hdr = new byte[16];
            int off = 0;
            while (off < hdr.Length)
            {
                int n = s.Read(hdr, off, hdr.Length - off);
                if (n <= 0) return false;
                off += n;
            }
            for (int i = 10; i < 16; i++) if (hdr[i] != 0) return false;
            long comp = BitConverter.ToUInt32(hdr, 4);
            long uncomp = BitConverter.ToUInt32(hdr, 0);
            return comp > 0 && uncomp > 0 && uncomp <= (1L << 31) && 16 + comp <= s.Length;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            var inflated = Inflate(file);
            if (TryOpenSquashfs(inflated, keepAlive, out var tree))
            {
                return tree;
            }
            return new IVfsNode[] { new PakVfsFile(inflated, Helpers.OctalLiteral(0444)) };
        });
    }

    private static MemoryVirtualFile Inflate(IVirtualFile file)
    {
        using var s = file.OpenRead();
        var buf = new byte[s.Length];
        int off = 0;
        while (off < buf.Length)
        {
            int n = s.Read(buf, off, buf.Length - off);
            if (n <= 0) break;
            off += n;
        }
        var data = new Memory64<byte>(buf[..off]);
        var decoded = new LzhsDecoder(data).DecodeToArray();
        return new MemoryVirtualFile(file.Name + ".bin", decoded);
    }

    private static bool TryOpenSquashfs(IVirtualFile inflated, ICollection<object> keepAlive, out List<IVfsNode> tree)
    {
        tree = new List<IVfsNode>();
        try
        {
            if (!VfsSquashfs.TryOpen(inflated, keepAlive, out var reader)) return false;
            var tmp = new YafexDirectory(inflated.Name, Helpers.OctalLiteral(0755));
            VfsSquashfs.BuildTree(tmp, reader);
            foreach (var kv in tmp.Tree) tree.Add(kv.Value.Node);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
