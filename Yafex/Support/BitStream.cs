using System;
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
