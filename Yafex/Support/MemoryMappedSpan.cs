using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace Yafex.Support
{
	public unsafe class MemoryMappedSpan<T> : MemoryManager<T>, IDisposable where T : unmanaged
	{
		public readonly long Length;

		private readonly MemoryMappedViewAccessor acc;
		private readonly byte* dptr = null;

		public MemoryMappedSpan(MemoryMappedFile mf, long length, bool readOnly = true) {
			this.Length = length;
			if (readOnly) {
				this.acc = mf.CreateViewAccessor(0, length, MemoryMappedFileAccess.Read);
			} else {
				this.acc = mf.CreateViewAccessor(0, length, MemoryMappedFileAccess.ReadWrite);
			}
			this.acc.SafeMemoryMappedViewHandle.AcquirePointer(ref dptr);
		}

		public override Span<T> GetSpan() {
			return new Span<T>((void*)dptr, (int)Length);
		}

		public override MemoryHandle Pin(int elementIndex = 0) {
			if (elementIndex < 0 || elementIndex >= Length) {
				throw new ArgumentOutOfRangeException(nameof(elementIndex));
			}

			return new MemoryHandle(dptr + elementIndex);
		}

		public override void Unpin() {}

		public void Dispose() {
			Dispose(true);
		}

		protected override void Dispose(bool disposing) {
			if(dptr != null) {
				acc.SafeMemoryMappedViewHandle.ReleasePointer();
			}
			acc.Dispose();
		}
	}
}
