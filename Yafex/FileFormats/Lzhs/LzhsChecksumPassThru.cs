using System.Collections.Generic;

namespace Smx.Yafex.FileFormats.Lzhs
{
	public class LzhsChecksumPassThru
	{
		private byte checksum = 0;

		public ushort Value => checksum;

		public IEnumerable<byte> Update(IEnumerable<byte> data) {
			foreach(var b in data) {
				checksum += b;
				yield return b;
			}
		}
	}
}
