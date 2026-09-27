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
using System.Security.Cryptography;

namespace Yafex.Fuse;

/// <summary>In-memory byte blob as a virtual file (inflated expander output).</summary>
public sealed class MemoryVirtualFile : IVirtualFile
{
    private readonly byte[] _content;

    public MemoryVirtualFile(string name, byte[] content)
    {
        Name = name;
        _content = content;
    }

    public string Name { get; }
    public long Length => _content.Length;
    public Stream OpenRead() => new MemoryStream(_content, writable: false);
}

/// <summary>Concatenation of source windows as one virtual file (firmware
/// volumes striped over pages, multi-segment modules). Reads only the
/// requested windows; seeks are free.</summary>
public sealed class MultiRangeVirtualFile : IVirtualFile
{
    private readonly IDataSource _source;
    private readonly (long Offset, long Length)[] _ranges;
    private readonly long _length;

    public MultiRangeVirtualFile(string name, IDataSource source, IEnumerable<(long Offset, long Length)> ranges)
    {
        Name = name;
        _source = source;
        _ranges = ranges is (long, long)[] arr ? arr : new List<(long, long)>(ranges).ToArray();
        long total = 0;
        foreach (var (_, len) in _ranges) total += len;
        _length = total;
    }

    public string Name { get; }
    public long Length => _length;
    public Stream OpenRead() => new MultiRangeStream(_source, _ranges, _length);
}

/// <summary>Windowed read-only stream over concatenated source ranges.</summary>
public sealed class MultiRangeStream : Stream
{
    private readonly IDataSource _source;
    private readonly (long Offset, long Length)[] _ranges;
    private readonly long _length;
    private long _position;

    public MultiRangeStream(IDataSource source, (long Offset, long Length)[] ranges, long length)
    {
        _source = source;
        _ranges = ranges;
        _length = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _length) return 0;
        count = (int)Math.Min(count, _length - _position);
        int total = 0;
        long pos = _position;
        foreach (var (start, len) in _ranges)
        {
            if (pos >= len)
            {
                pos -= len;
                continue;
            }
            int take = (int)Math.Min(len - pos, count - total);
            var window = _source.Data.Slice(start + pos, take).ToArray();
            window.CopyTo(buffer, offset + total);
            total += window.Length;
            if (total >= count) break;
            pos = 0;
        }
        _position += total;
        return total;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (target < 0 || target > _length) throw new IOException("seek out of range");
        _position = target;
        return target;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Windowed AES-CBC decrypting stream over a stored region.
/// CBC blocks decrypt independently given the previous ciphertext block
/// (or the IV at region start), so reads/seeks cost O(window) with no
/// buffering and no per-stream chaining state. A trailing partial block
/// is served raw-xored with <paramref name="xorTailKey"/> (Mediatek deXOR
/// tail rule); null serves it raw. One ECB decryptor per stream instance,
/// so concurrent opens are safe.</summary>
public sealed class AesCbcDecryptStream : Stream
{
    private readonly IDataSource _source;
    private readonly long _regionStart;
    private readonly long _regionLength;
    private readonly long _payloadSkip;
    private readonly long _length;
    private readonly byte[] _key;
    private readonly byte[] _iv;
    private readonly byte[]? _xorTailKey;
    private readonly long _tailStart;
    private readonly Aes _aes;
    private long _position;

    public AesCbcDecryptStream(IDataSource source, long regionStart, long regionLength,
        long payloadSkip, long length, byte[] key, byte[] iv, byte[]? xorTailKey = null)
    {
        _source = source;
        _regionStart = regionStart;
        _regionLength = regionLength;
        _payloadSkip = payloadSkip;
        _length = length;
        _key = key;
        _iv = iv;
        _xorTailKey = xorTailKey;
        _tailStart = regionLength & ~15L;
        _aes = Aes.Create();
        _aes.Key = key;
        _aes.Mode = CipherMode.ECB;
        _aes.Padding = PaddingMode.None;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _length) return 0;
        count = (int)Math.Min(count, _length - _position);

        // absolute payload coords -> containing cipher blocks
        long absStart = _payloadSkip + _position;
        long absEnd = absStart + count;
        long firstBlock = absStart / 16;
        long lastBlock = (absEnd - 1) / 16;

        using var dec = _aes.CreateDecryptor();
        var block = new byte[16];
        var prev = new byte[16];
        int total = 0;
        for (long b = firstBlock; b <= lastBlock; b++)
        {
            long blockAbs = b * 16;
            long copyFrom = Math.Max(absStart, blockAbs) - blockAbs;
            long copyTo = Math.Min(absEnd, blockAbs + 16) - blockAbs;
            if (blockAbs >= _tailStart)
            {
                // partial tail block: raw bytes, optionally deXORed with the
                // key from the tail start (Mediatek rule); never read past
                // the region end
                long want = Math.Min(copyTo - copyFrom,
                    _regionStart + _regionLength - (_regionStart + blockAbs + copyFrom));
                if (want <= 0) break;
                var raw = _source.Data.Slice(_regionStart + blockAbs + copyFrom, want).ToArray();
                for (long i = copyFrom; i < copyFrom + raw.Length; i++)
                {
                    byte v = raw[i - copyFrom];
                    if (_xorTailKey != null)
                    {
                        v ^= _xorTailKey[(blockAbs + i - _tailStart) % _xorTailKey.Length];
                    }
                    buffer[offset + total++] = v;
                }
                continue;
            }
            // previous ciphertext (or IV at region start) for CBC chaining
            if (blockAbs == 0)
            {
                _iv.CopyTo(prev, 0);
            }
            else
            {
                var got = _source.Data.Slice(_regionStart + blockAbs - 16, 16).ToArray();
                if (got.Length != 16) break;
                got.CopyTo(prev, 0);
            }
            var cipher = _source.Data.Slice(_regionStart + blockAbs, 16).ToArray();
            if (cipher.Length != 16) break;
            dec.TransformBlock(cipher, 0, 16, block, 0);
            for (int i = 0; i < 16; i++) block[i] ^= prev[i];

            for (long i = copyFrom; i < copyTo; i++)
            {
                buffer[offset + total++] = block[i];
            }
        }
        _position += total;
        return total;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (target < 0 || target > _length) throw new IOException("seek out of range");
        _position = target;
        return target;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _aes.Dispose();
        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
