using Yafex.Support;
using System.Collections.Generic;
using System.Linq;

namespace Yafex.FileFormats.Lzhs
{
	public class HuffmanDecoder
	{
		private IEnumerator<byte> it;
		private BitStream reader;

		public HuffmanDecoder(IEnumerable<byte> data) {
			it = data.GetEnumerator();
			reader = new BitStream(it);
		}

		private int GetSymbol(HuffmanSymbol sym, HuffmanSymbol[] table, HuffmanCacheBase cache) {
			int idx;
			// O(1)
			if(!cache.TryGetValue(sym, out idx)) {
				// O(n)
				var q = table
					.Select((sym, i) => (sym, i))
					.Where(it => it.sym == sym);

				idx = q.Any() ? q.First().i : -1;
				cache.Insert(sym, idx);
			}
			return idx;
		}

		private bool TryReadLZSSPos(out int pos) {
			var acc = new BitAccumulator(32);

			bool TryReadBit() {
				if (!reader.TryReadBit(out byte bit)) return false;
				acc.PushBit(bit);
				return true;
			}

			pos = default;

			// min symbol length is 2
			if (!TryReadBit() || !TryReadBit()) return false;
			
			while(acc.CurrentLength < 32) {
				var code = (uint)acc.GetValue();
				var sym = new HuffmanSymbol(code, acc.CurrentLength);
				var idx = GetSymbol(sym, LzhsTables.TblCharpos, LzhsCache.CharposCache);
				if(idx != -1) {
					pos = idx;
					return true;
				}

				if (!TryReadBit()) return false;
			}

			pos = 0;
			return false;
		}

		public bool TryReadLZSSLength(out int lzssLength) {
			var acc = new BitAccumulator(32);

			bool TryReadBit() {
				if (!reader.TryReadBit(out byte bit)) return false;
				acc.PushBit(bit);
				return true;
			}

			lzssLength = default;

			// min symbol length is 4
			if (!TryReadBit() || !TryReadBit() 
			 || !TryReadBit() || !TryReadBit()
			) {
				return false;
			}

			while(acc.CurrentLength < 32) {
				var code = (uint)acc.GetValue();
				var sym = new HuffmanSymbol(code, acc.CurrentLength);
				var idx = GetSymbol(sym, LzhsTables.TblCharlen, LzhsCache.CharLenCache);
				if(idx != -1) {
					lzssLength = idx;
					return true;
				}

				if (!TryReadBit()) return false;
			}
			
			lzssLength = 0;
			return false;
		}

		byte MakeFlags(LzssSequenceType[] sequence) {
			byte flag = 0;
			for(int i=0; i<sequence.Length; i++) {
				int bit = sequence[i] switch {
					LzssSequenceType.POINTER => 0,
					LzssSequenceType.RAW => 1
				};
				flag |= (byte)(bit << i);
			}
			return flag;
		}

		public IEnumerable<byte> Decode() {
			// worst case: 8 sequences of pointers (3 bytes each)
			// flag is excluded (written separately)
			List<byte> buf = new List<byte>(24);

			bool TryGetNextItem(out LzssSequenceType item) {
				item = default;

				if (!TryReadLZSSLength(out int codePoint)) {
					return false;
				}
				// if the codepoint lies within a byte range, it's raw data
				// if it lies outside, it's an lzss length element
				if (codePoint > 255) {
					// write length of LZSS item
					var lzssLength = codePoint - 256;
					buf.Add((byte)lzssLength);

					// read huffman encoded byte1 position
					if (!TryReadLZSSPos(out int lzssPos1)) {
						return false;
					}
					buf.Add((byte)(lzssPos1 >> 1));

					var acc = new BitAccumulator(7);
					for(int i=0; i<acc.MaxLength; i++) {
						if(!reader.TryReadBit(out byte bit)) {
							return false;
						}
						acc.PushBit(bit);
					}
					int lzssPos0 = (int)(
						(uint)acc.GetValue()
						| ((uint)lzssPos1 << 7)
					);

					buf.Add((byte)(
						(lzssPos1 << 7) | lzssPos0
					));

					item = LzssSequenceType.POINTER;
				} else {
					// raw data
					buf.Add((byte)codePoint);
					item = LzssSequenceType.RAW;
				}
				return true;
			}
	
			bool stop = false;
			while (!stop) {
				byte flags = 0;
				for (int i = 0; i < 8; i++) {
					if (!TryGetNextItem(out var item)) {
						stop = true;
						break;
					}
					flags |= (byte)((byte)item << i);
				}

				/** flush buffer **/
				// write flag
				yield return flags;
				// write data
				foreach (var b in buf) yield return b;
				buf.Clear();
			}
		}
	}
}
