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

namespace Yafex.FileFormats
{
    /// <summary>
    /// Describes a single encrypted (or plaintext) data region of a virtual package,
    /// and how it maps to the logical (decrypted) package content.
    /// </summary>
    public class PakSegment
    {
        /// <summary>Offset in the source where this segment's data region starts.</summary>
        public required long PhysicalDataOffset;

        /// <summary>Size of the data region as stored in the source (may include a leading index prefix).</summary>
        public required long EncryptedSize;

        /// <summary>Offset within the decrypted stream where the logical content begins.</summary>
        public required long ContentOffset;

        /// <summary>Number of logical bytes this segment contributes to the package.</summary>
        public required long LogicalSize;

        /// <summary>Whether the segment data is encrypted (and must be decrypted before use).</summary>
        public required bool WasDecrypted;

        /// <summary>Decryptor for the segment data, if <see cref="WasDecrypted"/> is set.</summary>
        public AesDecryptor? Decryptor;
    }

    /// <summary>
    /// A virtual file: an ordered list of <see cref="PakSegment"/>s whose logical content
    /// is the concatenation of each segment's content.
    /// </summary>
    public class PakImage : IVirtualFile
    {
        public string Name = "";
        string IVirtualFile.Name => Name;

        public IBlockSource Source = null!;

        public List<PakSegment> Segments = new();

        public long Length
        {
            get
            {
                long len = 0;
                foreach (var seg in Segments) len += seg.LogicalSize;
                return len;
            }
        }

        public PakStream OpenStream() => new PakStream(this);

        Stream IVirtualFile.OpenRead() => OpenStream();

        /// <summary>Stored byte count (payload regions as kept in the source, no decrypt).</summary>
        public long RawLength
        {
            get
            {
                long len = 0;
                foreach (var seg in Segments) len += seg.EncryptedSize;
                return len;
            }
        }

        /// <summary>Stored bytes as kept in the source (e.g. still encrypted), no decrypt.</summary>
        public Stream OpenRawStream() => new RawPakStream(this);

        /// <summary>Stored-bytes view of this image for raw VFS nodes.</summary>
        public IVirtualFile AsRaw() => new RawPakView(this);

        private sealed class RawPakView : IVirtualFile
        {
            private readonly PakImage _image;
            public RawPakView(PakImage image) { _image = image; }
            public string Name => _image.Name;
            public long Length => _image.RawLength;
            public Stream OpenRead() => _image.OpenRawStream();
        }
    }

    /// <summary>
    /// Forward-readable stream over the stored ( undecrypted) segment regions.
    /// </summary>
    public sealed class RawPakStream : Stream
    {
        private readonly PakImage _image;
        private readonly IBlockSource _source;
        private long _position;

        public RawPakStream(PakImage image)
            : this(image, image.Source)
        {
        }

        public RawPakStream(PakImage image, IBlockSource source)
        {
            _image = image;
            _source = source;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _image.RawLength;
        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > Length) throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= Length) return 0;
            count = (int)Math.Min(count, Length - _position);
            var total = 0;
            var segBase = 0L;
            foreach (var seg in _image.Segments)
            {
                var segEnd = segBase + seg.EncryptedSize;
                if (_position + total >= segEnd)
                {
                    segBase = segEnd;
                    continue;
                }
                var inSeg = _position + total - segBase;
                var take = (int)Math.Min(count - total, seg.EncryptedSize - inSeg);
                var n = _source.Read(seg.PhysicalDataOffset + inSeg, buffer, offset + total, take);
                if (n <= 0) break;
                total += n;
                segBase = segEnd;
                if (total >= count) break;
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
                SeekOrigin.End => Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            Position = target;
            return target;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Read-only random access over the physical source data.
    /// </summary>
    public interface IBlockSource
    {
        long Length { get; }

        byte[] Read(long offset, int count);

        int Read(long offset, byte[] buffer, int bufferOffset, int count);
    }

    /// <summary>
    /// <see cref="IBlockSource"/> backed by a memory-mapped region (MFile).
    /// Reads are position-independent and safe to perform concurrently.
    /// </summary>
    public class MappedBlockSource : IBlockSource
    {
        private readonly Memory64<byte> _data;

        public MappedBlockSource(Memory64<byte> data)
        {
            _data = data;
        }

        public Memory64<byte> Data => _data;

        public long Length => _data.Length;

        public byte[] Read(long offset, int count)
        {
            if (offset >= _data.Length) return Array.Empty<byte>();
            count = (int)Math.Min(count, _data.Length - offset);
            return _data.Slice(offset, count).ToArray();
        }

        public int Read(long offset, byte[] buffer, int bufferOffset, int count)
        {
            if (offset >= _data.Length) return 0;
            count = (int)Math.Min(count, _data.Length - offset);
            if (count <= 0) return 0;
            _data.Slice(offset, count).ToArray().CopyTo(buffer, bufferOffset);
            return count;
        }
    }

    /// <summary>
    /// A lazy, decrypt-on-demand <see cref="Stream"/> over a <see cref="PakImage"/>.
    /// Only the AES blocks overlapping the requested range are read from the source
    /// and decrypted, so the package is never materialized in memory.
    /// </summary>
    public class PakStream : Stream
    {
        private const int AES_BLOCK_SIZE = 16;

        private readonly PakImage _image;
        private readonly IBlockSource _source;
        private readonly object _gate = new object();

        private long _position;

        public PakStream(PakImage image, IBlockSource source)
        {
            _image = image;
            _source = source;
        }

        public PakStream(PakImage image)
            : this(image, image.Source)
        {
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;

        public override long Length => _image.Length;

        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                _position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
            {
                throw new ArgumentOutOfRangeException();
            }

            lock (_gate)
            {
                if (_position >= Length) return 0;
                count = (int)Math.Min(count, Length - _position);

                int total = 0;
                while (count > 0)
                {
                    var (segment, segOffset) = FindSegment(_position, out long segPos);
                    int take = (int)Math.Min(count, segment.LogicalSize - segPos);

                    int n = ReadSegment(segment, segPos, buffer, offset, take);
                    if (n <= 0) break;

                    offset += n;
                    _position += n;
                    total += n;
                    count -= n;
                }
                return total;
            }
        }

        private (PakSegment, long) FindSegment(long position, out long segPos)
        {
            long cumulative = 0;
            foreach (var seg in _image.Segments)
            {
                if (position < cumulative + seg.LogicalSize)
                {
                    segPos = position - cumulative;
                    return (seg, segPos);
                }
                cumulative += seg.LogicalSize;
            }
            segPos = 0;
            return (null!, 0);
        }

        private int ReadSegment(PakSegment segment, long segPos, byte[] buffer, int offset, int take)
        {
            if (take <= 0) return 0;

            if (!segment.WasDecrypted || segment.Decryptor == null)
            {
                // plaintext passthrough
                return _source.Read(segment.PhysicalDataOffset + segment.ContentOffset + segPos, buffer, offset, take);
            }

            // AES covers whole blocks only; a trailing (length % 16) remainder
            // is stored raw and read through directly (EPKv3 chunks).
            long segTotal = segment.ContentOffset + segment.LogicalSize;
            long encLen = segTotal & ~(AES_BLOCK_SIZE - 1);

            long d = segment.ContentOffset + segPos;
            int total = 0;
            if (d < encLen)
            {
                int encTake = (int)Math.Min(take, encLen - d);
                var blockStart = d & ~(AES_BLOCK_SIZE - 1);
                var blockEnd = Math.Min((d + encTake + AES_BLOCK_SIZE - 1) & ~(AES_BLOCK_SIZE - 1L), encLen);

                bool cbc = segment.Decryptor.Aes.Mode == System.Security.Cryptography.CipherMode.CBC;
                var discardPrefixBlocks = (cbc && blockStart > 0) ? 1 : 0;
                var readStart = blockStart - (long)(discardPrefixBlocks * AES_BLOCK_SIZE);

                var cipherLen = (int)(blockEnd - readStart);
                var ciphertext = _source.Read(segment.PhysicalDataOffset + readStart, cipherLen);
                if (ciphertext.Length != cipherLen)
                {
                    return total;
                }

                var plaintext = new byte[cipherLen];
                segment.Decryptor.DecryptBlocks(ciphertext, discardPrefixBlocks, plaintext);

                var src = (int)(d - blockStart);
                Array.Copy(plaintext, src, buffer, offset, encTake);

                total += encTake;
                d += encTake;
                offset += encTake;
                take -= encTake;
            }
            if (take > 0)
            {
                int n = _source.Read(segment.PhysicalDataOffset + d, buffer, offset, take);
                if (n <= 0) return total;
                total += n;
            }
            return total;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    target = offset;
                    break;
                case SeekOrigin.Current:
                    target = _position + offset;
                    break;
                case SeekOrigin.End:
                    target = Length + offset;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(origin));
            }
            if (target < 0) throw new IOException("Attempted to seek before the start of the stream");
            Position = target;
            return target;
        }

        public override void Flush() { }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
