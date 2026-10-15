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

using Yafex.FileFormats.Sparse;

namespace Yafex.Fuse;

/// <summary>Lazy raw view of an Android sparse image: reads translate to
/// chunk payloads (raw slices, fill words, zeros) without inflating.
/// Length is total_blks * blk_sz and may exceed RAM by design.</summary>
public sealed class SparseImageVirtualFile : IVirtualFile
{
    private readonly IVirtualFile _source;
    private readonly SparseLayout _layout;

    public SparseImageVirtualFile(string name, IVirtualFile source, SparseLayout layout)
    {
        Name = name;
        _source = source;
        _layout = layout;
    }

    public string Name { get; }
    public long Length => _layout.OutputLength;
    public Stream OpenRead() => new SparseImageStream(_source, _layout);
}

public sealed class SparseImageStream : Stream
{
    private readonly Stream _source;
    private readonly SparseLayout _layout;
    private long _position;

    public SparseImageStream(IVirtualFile source, SparseLayout layout)
    {
        _source = source.OpenRead();
        _layout = layout;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _layout.OutputLength;
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

    private int FindChunk(long pos)
    {
        var chunks = _layout.Chunks;
        int lo = 0, hi = chunks.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            var c = chunks[mid];
            if (pos < c.OutOffset) hi = mid - 1;
            else if (pos >= c.OutOffset + c.OutLength) lo = mid + 1;
            else return mid;
        }
        return -1;
    }

    private void ReadSource(long fileOff, byte[] buffer, int offset, int count)
    {
        _source.Seek(fileOff, SeekOrigin.Begin);
        int got = _source.ReadAtLeast(buffer.AsSpan(offset, count), count, throwOnEndOfStream: false);
        if (got != count) throw new IOException("sparse payload truncated");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= Length) return 0;
        count = (int)Math.Min(count, Length - _position);
        int total = 0;
        while (total < count)
        {
            int ci = FindChunk(_position);
            if (ci < 0) throw new IOException("sparse position outside chunks");
            var c = _layout.Chunks[ci];
            int take = (int)Math.Min(count - total, c.OutOffset + c.OutLength - _position);
            switch (c.Type)
            {
                case SparseParser.Raw:
                    ReadSource(c.FileOffset + (_position - c.OutOffset), buffer, offset + total, take);
                    break;
                case SparseParser.Fill:
                {
                    byte b0 = (byte)(c.Fill & 0xFF);
                    byte b1 = (byte)((c.Fill >> 8) & 0xFF);
                    byte b2 = (byte)((c.Fill >> 16) & 0xFF);
                    byte b3 = (byte)((c.Fill >> 24) & 0xFF);
                    long rel = _position - c.OutOffset;
                    for (int i = 0; i < take; i++)
                    {
                        int m = (int)((rel + i) % 4);
                        buffer[offset + total + i] = m == 0 ? b0 : m == 1 ? b1 : m == 2 ? b2 : b3;
                    }
                    break;
                }
                default:
                    Array.Clear(buffer, offset + total, take);
                    break;
            }
            total += take;
            _position += take;
        }
        return total;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (target < 0 || target > Length) throw new IOException("seek out of range");
        _position = target;
        return target;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _source.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Mount expander for Android sparse images: exposes the inflated
/// raw image (lazily chunk-mapped), chaining into nested-format expansion
/// (filesystems inside the raw image).</summary>
public sealed class SparseExpander : IContentExpander
{
    public string Name => "sparse";

    public bool Matches(IVirtualFile file)
    {
        try
        {
            if (file.Length < SparseParser.HeaderSize) return false;
            var head = file.ReadHead(1 << 20);
            if (TryParse(head, out _)) return true;
            // The chunk walk may extend past a 1MB probe; fall back to a
            // bounded full read for larger images.
            if (file.Length > 512 << 20) return false;
            return TryParse(file.ReadAvailable(), out _);
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static bool TryParse(byte[] image, out SparseLayout? layout)
    {
        layout = null;
        try
        {
            layout = SparseParser.Open(image);
            return layout != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public IVirtualFile ImageFile(IVirtualFile file, ICollection<object> keepAlive)
    {
        var layout = SparseParser.Open(file.ReadAvailable())
            ?? throw new InvalidDataException("sparse image no longer parses cleanly");
        var image = new SparseImageVirtualFile(file.Name + ".raw", file, layout);
        keepAlive.Add(image);
        return image;
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            var nodes = new List<IVfsNode>();
            VfsBuilder.AddWithNested(nodes, ImageFile(file, keepAlive), keepAlive);
            return nodes;
        });
    }
}
