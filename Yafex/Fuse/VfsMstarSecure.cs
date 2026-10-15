#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
using System;
using System.IO;

using Yafex.FileFormats.MstarSecure;

namespace Yafex.Fuse;

// No content expander for this format on purpose: expansion needs the
// staging key, and expanders are keyless by design. The addon builds the
// decrypted tree directly (see MstarSecureAddon.CreateVfs).

/// <summary>Decrypted view of an MStar secure container: stored header plus
/// AES-CBC payload, decrypted one segment at a time on demand (RAM stays
/// O(segment); seeks are free since every segment carries its own IV
/// chaining from the previous ciphertext tail).</summary>
public sealed class MstarSecureVirtualFile : IVirtualFile
{
    private readonly IDataSource _source;
    private readonly MstarSecure.Chunk _chunk;
    private readonly byte[] _key;

    public MstarSecureVirtualFile(string name, IDataSource source, MstarSecure.Chunk chunk, byte[] key)
    {
        Name = name;
        _source = source;
        _chunk = chunk;
        _key = key;
    }

    public string Name { get; }

    public long Length => _chunk.DataOffset + _chunk.DataLength;

    public Stream OpenRead() => new MstarSecureDecodeStream(_source, _chunk, _key);
}

/// <summary>Random-access decrypt stream over an <see cref="MstarSecure"/>
/// container: header passthrough, per-segment decrypt with a one-segment
/// cache.</summary>
public sealed class MstarSecureDecodeStream : Stream
{
    private readonly IDataSource _source;
    private readonly MstarSecure.Chunk _chunk;
    private readonly byte[] _key;
    private long _position;
    private long _cachedIndex = -1;
    private byte[] _cachedPlain = [];
    private long _cachedLength;

    public MstarSecureDecodeStream(IDataSource source, MstarSecure.Chunk chunk, byte[] key)
    {
        _source = source;
        _chunk = chunk;
        _key = key;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _chunk.DataOffset + _chunk.DataLength;
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= Length) return 0;
        count = (int)Math.Min(count, Length - _position);
        int total = 0;
        while (total < count)
        {
            if (_position < _chunk.DataOffset)
            {
                int take = (int)Math.Min(count - total, _chunk.DataOffset - _position);
                var window = _source.Data.Slice(_position, take).ToArray();
                window.CopyTo(buffer, offset + total);
                total += take;
                _position += take;
                continue;
            }
            long rel = _position - _chunk.DataOffset;
            long index = rel / _chunk.SegmentSize;
            EnsureSegment(index);
            long segStart = _chunk.DataOffset + index * _chunk.SegmentSize;
            long segLen = Math.Min(_cachedLength, Length - segStart);
            int avail = (int)(segLen - (rel - index * _chunk.SegmentSize));
            int want = Math.Min(avail, count - total);
            Array.Copy(_cachedPlain, rel - index * _chunk.SegmentSize, buffer, offset + total, want);
            total += want;
            _position += want;
        }
        return total;
    }

    private void EnsureSegment(long index)
    {
        if (index == _cachedIndex) return;
        // Windowed read: only the segment plus its 16-byte IV tail.
        long off = _chunk.DataOffset + index * _chunk.SegmentSize;
        int take = (int)Math.Min(_chunk.SegmentSize, _source.Data.Length - off);
        var cipher = _source.Data.Slice(off, take).ToArray();
        byte[]? prevTail = null;
        if (index > 0)
        {
            prevTail = _source.Data.Slice(off - 16, 16).ToArray();
        }
        var plain = MstarSecure.DecryptSegment(cipher, index, _key, prevTail);
        _cachedPlain = plain;
        _cachedLength = plain.Length;
        // The last segment may extend past DataLength (short on-disk tail
        // is trimmed by readers via Length, matching DecryptAll).
        _cachedIndex = index;
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
}
