namespace Yafex.FileFormats.Lzhs
{
	public class BitAccumulator
	{
		private ulong accumulator = 0;
		private int curLength = 0;
		private int maxLength;

		public int CurrentLength => curLength;
		public int MaxLength => maxLength;

		public BitAccumulator(int length) {
			this.maxLength = length;
		}

		public void PushBit(int bit) {
			int bitPos = maxLength - (curLength + 1);
			accumulator |= (ulong)bit << bitPos;
			++curLength;
		}

		public ulong GetValue() {
			int shamt = maxLength - curLength;
			ulong mask = (ulong)((1 << curLength) - 1);
			ulong value = (accumulator >> shamt) & mask;
			return value;
		}
	}
}
