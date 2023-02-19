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

namespace Yafex.Support
{
	public class BitStream : IDisposable
	{
		private readonly IEnumerator<byte> stream;

		private byte b = 0;
		private int bitPos = -1;

		private int bitsRead = 0;

		public BitStream(IEnumerator<byte> data) {
			this.stream = data;
		}

		private bool TryFetchByte(out byte b) {
			if (!stream.MoveNext()) {
				b = default;
				return false;
			}
			b = stream.Current;
			return true;
		}

		public byte ReadBit() {
			if(!TryReadBit(out byte bit)) {
				throw new EndOfStreamException();
			}
			return bit;
		}

		public bool TryReadBit(out byte bit) {
			if (bitPos < 0 || bitPos >= 7) {
				if(!TryFetchByte(out b)) {
					bit = default;
					return false;
				}
				bitPos = -1;
			}

			++bitPos;

			// LSB Mode
			//int value = (b >> bitPos) & 1;

			// MSB Mode
			int value = (b >> (7 - bitPos)) & 1;

			++bitsRead;
			bit = (byte)value;
			return true;
		}

		public void Dispose() {
			stream.Dispose();
		}
	}
}
