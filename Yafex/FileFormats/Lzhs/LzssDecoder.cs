using System.Collections.Generic;

namespace Smx.Yafex.FileFormats.Lzhs
{
	public class LzssDecoder {
		const int N = 4096;
		const int F = 34;
		const int THRESHOLD = 2;

		private RingBuffer<byte> ringBuf = new RingBuffer<byte>(N + F - 1, N - 1u);
		private ModCounter bufPos = new ModCounter(0, N - 1u);

		private IEnumerator<byte> it;

		public LzssDecoder(IEnumerable<byte> data) {
			ringBuf.Fill(0);
			it = data.GetEnumerator();
		}

		private LzssSequenceType[] ReadSequence(byte flag) {
			var seq = new LzssSequenceType[8];

			// shift register
			byte shr = flag;

			for (int i = 0; i < 8; i++) {
				seq[i] = (shr & 1u) switch {
					0 => LzssSequenceType.POINTER,
					1 => LzssSequenceType.RAW
				};
				shr >>= 1;
			}

			return seq;
		}

		private bool TryReadByte(out byte b) {
			if (!it.MoveNext()) {
				b = 0;
				return false;
			}
			b = it.Current;
			return true;
		}

		private IEnumerable<byte> DecodeRaw() {
			if (!TryReadByte(out byte b)) {
				yield break;
			}
			ringBuf[bufPos++.value] = b;
			yield return b;
		}

		private IEnumerable<byte> DecodePointer() {
			if (!TryReadByte(out byte matchLen)) yield break;
			if (!TryReadByte(out byte b1)) yield break;
			if (!TryReadByte(out byte b0)) yield break;

			var matchPos = (b1 << 8) | b0;
			for (int i = 0; i <= matchLen + THRESHOLD; i++) {
				byte b = ringBuf[(bufPos - matchPos).value];
				ringBuf[bufPos++.value] = b;
				yield return b;
			}
		}

		public IEnumerable<byte> Decode() {
			while (TryReadByte(out byte flags)) {
				foreach (var i in ReadSequence(flags)) {
					switch (i) {
						case LzssSequenceType.POINTER:
							foreach (var b in DecodePointer()) yield return b;
							break;
						case LzssSequenceType.RAW:
							foreach (var b in DecodeRaw()) yield return b;
							break;
					}
				}
			}
		}
	}
}
