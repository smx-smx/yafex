using System.Collections.Generic;

namespace Smx.Yafex.FileFormats.Lzhs
{
	public class ArmThumbConvert
	{
		private IEnumerator<byte> it;

		public ArmThumbConvert(IEnumerable<byte> data) {
			it = data.GetEnumerator();
		}

		private bool TryReadByte(out byte b) {
			if (!it.MoveNext()) {
				b = 0;
				return false;
			}
			b = it.Current;
			return true;
		}

		private byte[] InsnFetch() {
			if (!TryReadByte(out byte b0)) return new byte[0];
			if (!TryReadByte(out byte b1)) return new byte[] { b0 };
			return new byte[] { b0, b1 };
		}

		private bool IsValidThumbInsnSeq0(byte[] insn) {
			return insn.Length == 2 && (insn[1] & 0xF8) == 0xF0;
		}

		private bool IsValidThumbInsnSeq1(byte[] insn) {
			return insn.Length == 2 && (insn[1] & 0xF8) == 0xF8;
		}

		private byte[] TransformThumbPair(
			byte[] insn1, byte[] insn2,
			uint nowPos, uint curPos, bool encoding
		) {
			var src = (
				((insn1[1] & 0x7) << 19)
				| (insn1[0] << 11)
				| ((insn2[1] & 0x7) << 8)
				| insn2[0]
			) << 1;

			var dest = encoding switch {
				true => nowPos + curPos + 4 + src,
				false => src - (nowPos + curPos + 4)
			} >> 1;

			var data = new byte[] {
				(byte)(dest >> 11),
				(byte)(0xF0 | ((dest >> 19) & 0x7)),
				(byte)dest,
				(byte)(0xF8 | (dest >> 8) & 0x7)
			};
			return data;
		}

		private IEnumerable<byte> Convert(uint nowPos, bool encoding) {
			uint pos = 0;
			var curInsn = InsnFetch();
			var nextInsn = InsnFetch();

			while (true) {
				if(curInsn.Length < 2) {
					foreach (var b in curInsn) yield return b;
					yield break;
				}
				if(nextInsn.Length < 2) {
					foreach (var b in curInsn) yield return b;
					foreach (var b in nextInsn) yield return b;
					yield break;
				}

				if(IsValidThumbInsnSeq0(curInsn)
					&& IsValidThumbInsnSeq1(nextInsn)
				) {
					var converted = TransformThumbPair(curInsn, nextInsn, nowPos, pos, encoding);
					foreach (var b in converted) yield return b;

					curInsn = InsnFetch();
					nextInsn = InsnFetch();
					pos += 4;
				} else {
					foreach (var b in curInsn) yield return b;
					curInsn = nextInsn;
					nextInsn = InsnFetch();
					pos += 2;
				}
			}
		}

		public IEnumerable<byte> Decode(uint nowPos) {
			return Convert(nowPos, false);
		}
	}
}
