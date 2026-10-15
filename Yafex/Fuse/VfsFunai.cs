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

using Yafex.FileFormats.Funai;

namespace Yafex.Fuse;

/// <summary>Decrypted view of one Funai UPG/PHL payload: the payload is the
/// transform unit (blocks relative to the payload start, trailing bytes pass
/// through). Reads decrypt exactly the requested 8-byte-aligned window.</summary>
public sealed class FunaiPayloadFile : IVirtualFile
{
    private readonly IDataSource _source;
    private readonly long _offset;
    private readonly bool _encrypted;
    private readonly uint _key;

    public FunaiPayloadFile(string name, IDataSource source, long offset, long length, bool encrypted, uint key)
    {
        Name = name;
        _source = source;
        _offset = offset;
        Length = length;
        _encrypted = encrypted;
        _key = key;
    }

    public string Name { get; }
    public long Length { get; }

    public Stream OpenRead()
    {
        if (!_encrypted) return new DataSourceRangeStream(_source, _offset, Length);
        return new FunaiPayloadStream(_source, _offset, Length, _key);
    }
}

public sealed class FunaiPayloadStream : Stream
{
    private readonly IDataSource _source;
    private readonly long _base;
    private readonly long _length;
    private readonly uint _key;
    private long _position;

    public FunaiPayloadStream(IDataSource source, long offset, long length, uint key)
    {
        _source = source;
        _base = offset;
        _length = length;
        _key = key;
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
        // Payload-relative 8-byte blocks; trim edges after decrypt.
        long relAligned = (_position / 8) * 8;
        int headSkip = (int)(_position - relAligned);
        long relEnd = _position + count;
        long tailAligned = ((relEnd + 7) / 8) * 8;
        long windowLen = Math.Min(tailAligned - relAligned, _length - relAligned);
        var cipher = _source.Data.Slice(_base + relAligned, (int)windowLen).ToArray();
        var plain = FunaiCrypto.Decrypt(cipher, _key);
        int take = Math.Min(count, plain.Length - headSkip);
        Array.Copy(plain, headSkip, buffer, offset, take);
        _position += take;
        return take;
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

/// <summary>Lazy BDP partition: the blob is decrypted file-aligned on first
/// access, then cmpr/zlib/plain-processed and memoized. Length triggers the
/// same one-time processing.</summary>
public sealed class FunaiBdpPartitionFile : IVirtualFile
{
    private readonly IDataSource _source;
    private readonly long _blobOffset;
    private readonly long _blobSize;
    private readonly uint _key;
    private readonly object _gate = new();
    private byte[]? _memo;
    private bool _failed;
    private Exception? _error;

    public FunaiBdpPartitionFile(string name, IDataSource source, long blobOffset, long blobSize, uint key)
    {
        Name = name;
        _source = source;
        _blobOffset = blobOffset;
        _blobSize = blobSize;
        _key = key;
    }

    public string Name { get; }

    public long Length
    {
        get
        {
            EnsureProcessed();
            return _memo!.Length;
        }
    }

    public Stream OpenRead()
    {
        EnsureProcessed();
        return new MemoryStream(_memo!, writable: false);
    }

    private void EnsureProcessed()
    {
        if (_memo != null) return;
        lock (_gate)
        {
            if (_memo != null) return;
            if (_failed) throw new InvalidDataException($"BDP partition '{Name}' failed to process", _error);
            try
            {
                var file = _source.Data.ToArray();
                var blob = FunaiBdpParser.DecryptBlobRange(file, _blobOffset, (int)_blobSize, file.Length, _key);
                _memo = FunaiBdpParser.ProcessBlob(blob);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException || ex is IOException)
            {
                _failed = true;
                _error = ex;
                throw new InvalidDataException($"BDP partition '{Name}' failed to process: {ex.Message}", ex);
            }
        }
    }
}
