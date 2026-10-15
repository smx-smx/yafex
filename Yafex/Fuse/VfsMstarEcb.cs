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
using System.Security.Cryptography;

using Yafex.FileFormats.MstarEcb;

namespace Yafex.Fuse;

// No content expander for this format on purpose: expansion needs the
// staging key, and expanders are keyless by design. The addon builds the
// decrypted tree directly (see MstarEcbAddon.CreateVfs).

/// <summary>Decrypted view of an old-variant MStar image: ECB blocks are
/// independent, so every read decrypts exactly its 16-byte-aligned window
/// (no cache, no chaining).</summary>
public sealed class MstarEcbVirtualFile : IVirtualFile
{
    private readonly IDataSource _source;
    private readonly byte[] _key;

    public MstarEcbVirtualFile(string name, IDataSource source, byte[] key)
    {
        Name = name;
        _source = source;
        _key = key;
    }

    public string Name { get; }

    public long Length => _source.Data.Length;

    public Stream OpenRead() => new MstarEcbDecodeStream(_source, _key);
}

/// <summary>Position-independent ECB decrypt stream.</summary>
public sealed class MstarEcbDecodeStream : Stream
{
    private readonly IDataSource _source;
    private readonly byte[] _key;
    private long _position;

    public MstarEcbDecodeStream(IDataSource source, byte[] key)
    {
        _source = source;
        _key = key;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _source.Data.Length;
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= Length) return 0;
        count = (int)Math.Min(count, Length - _position);
        // Align the window to 16-byte blocks; trim the edges after.
        long aligned = _position & ~15L;
        int headSkip = (int)(_position - aligned);
        int tail = (int)((_position + count + 15) & ~15L);
        int window = (int)Math.Min(tail - aligned, Length - aligned);
        var cipher = _source.Data.Slice(aligned, window).ToArray();
        using var aes = Aes.Create();
        aes.BlockSize = 128;
        aes.KeySize = _key.Length * 8;
        aes.Mode = CipherMode.ECB;
        aes.Key = _key;
        aes.Padding = PaddingMode.None;
        var plain = aes.CreateDecryptor().TransformFinalBlock(cipher, 0, cipher.Length);
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
