#region License
/*
 * Copyright (c) 2023 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yafex.Cygwin
{
	public class CygwinOutputStream2 : Stream
	{
		private const int BUFFER_SIZE = 4096;

		private readonly int fd;

		private int written = 0;
		private byte[] buffer;
		private MemoryStream mem;

		public CygwinOutputStream2(int fd) {
			this.fd = fd;
			this.buffer = new byte[BUFFER_SIZE];
			this.mem = new MemoryStream(buffer);
		}

		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;

		public override long Length {
			get {
				throw new NotSupportedException();
			}
		}

		public override long Position {
			get {
				throw new NotSupportedException();
			}

			set {
				throw new NotSupportedException();
			}
		}

		public override int Read(byte[] buffer, int offset, int count) {
			throw new NotSupportedException();
		}

		public override long Seek(long offset, SeekOrigin origin) {
			throw new NotSupportedException();
		}

		public override void SetLength(long value) {
			throw new NotSupportedException();
		}

		public override void Flush() {
			Cygwin.Write(fd, this.buffer, 0, this.written);
			this.written = 0;
			mem.Position = 0;
		}


		public override void Write(byte[] buffer, int offset, int count) {
			int max = BUFFER_SIZE - this.written;
			int toWrite = Math.Min(count, max);

			mem.Write(buffer, offset, toWrite);
			this.written += toWrite;

			if (buffer.Contains((byte)'\n') || toWrite < count) {
				Flush();
			}

			int remaining = count - toWrite;
			if(remaining > 0){
				mem.Write(buffer, offset + toWrite, remaining);
				this.written += remaining;
			}
		}
	}
}
