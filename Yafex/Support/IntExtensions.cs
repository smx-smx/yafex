using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Yafex.Support
{
	public static class IntExtensions
	{
		public static uint BigEndianToHost(this uint n) {
			if (!BitConverter.IsLittleEndian) {
				return n;
			}
			return BinaryPrimitives.ReverseEndianness(n);
		}
	}
}
