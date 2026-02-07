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
using System.Buffers;
using System.IO.MemoryMappedFiles;

using Smx.SharpIO.Memory.Buffers;

namespace Yafex.Support
{
    public unsafe class MemoryMappedSpan<T> : MemoryManager64<T>, IDisposable where T : unmanaged
    {
        public readonly long Length;

        private readonly MemoryMappedViewAccessor acc;
        private readonly byte* dptr = null;

        public MemoryMappedSpan(MemoryMappedFile mf, long length, bool readOnly = true)
        {
            this.Length = length;
            if (readOnly)
            {
                this.acc = mf.CreateViewAccessor(0, length, MemoryMappedFileAccess.Read);
            }
            else
            {
                this.acc = mf.CreateViewAccessor(0, length, MemoryMappedFileAccess.ReadWrite);
            }
            this.acc.SafeMemoryMappedViewHandle.AcquirePointer(ref dptr);
        }

        public override Span64<T> GetSpan()
        {
            return new Span64<T>((void*)dptr, Length);
        }

        public override MemoryHandle Pin(long elementIndex = 0)
        {
            if (elementIndex < 0 || elementIndex >= Length)
            {
                throw new ArgumentOutOfRangeException(nameof(elementIndex));
            }

            return new MemoryHandle(dptr + elementIndex);
        }

        public override void Unpin() { }

        public void Dispose()
        {
            Dispose(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (dptr != null)
            {
                acc.SafeMemoryMappedViewHandle.ReleasePointer();
            }
            acc.Dispose();
        }
    }
}
