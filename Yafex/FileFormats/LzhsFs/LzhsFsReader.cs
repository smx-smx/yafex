using Yafex.FileFormats.Lzhs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yafex.FileFormats.LzhsFs
{
	public record LzhsChunk(ushort index, int size, int outputOffset, Memory<byte> buf)
	{
		public readonly LzhsHeader Header = new LzhsHeader(buf.Span);
		public LzhsDecoder NewDecoder() => new LzhsDecoder(buf);
	}
}
