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

namespace Yafex.FileSystems.Base.Streams
{
    /// <summary>
    /// Read-only <see cref="IBuffer"/> over a fully decoded file body, for
    /// drivers that inflate/assemble content up front (jffs2, ubifs, ufs,
    /// squashfs 2/3, vdfs).
    /// </summary>
    public sealed class ReadOnlyByteBuffer : IBuffer
    {
        /// <summary>Zero-length content (directories, symlinks).</summary>
        public static readonly ReadOnlyByteBuffer Empty = new(Array.Empty<byte>());

        private readonly byte[] _data;

        public ReadOnlyByteBuffer(byte[] data)
        {
            _data = data;
        }

        public bool CanRead => true;

        public bool CanWrite => false;

        public long Capacity => _data.Length;

        public IEnumerable<StreamExtent> Extents
        {
            get { yield return new StreamExtent(0, _data.Length); }
        }

        public int Read(long pos, byte[] buffer, int offset, int count)
        {
            if (pos >= _data.Length)
            {
                return 0;
            }
            int take = (int)Math.Min(count, _data.Length - pos);
            Array.Copy(_data, pos, buffer, offset, take);
            return take;
        }

        public void Write(long pos, byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public void Clear(long pos, int count) => throw new NotSupportedException();

        public void Flush() { }

        public void SetCapacity(long value) => throw new NotSupportedException();

        public IEnumerable<StreamExtent> GetExtentsInRange(long start, long count)
        {
            if (start < _data.Length)
            {
                yield return new StreamExtent(start, Math.Min(count, _data.Length - start));
            }
        }
    }
}
