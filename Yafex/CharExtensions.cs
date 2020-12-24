using System;
using System.Collections.Generic;
using System.Text;

namespace Smx.Yafex
{
	public static class CharExtensions
	{
		public static bool IsHexDigit(this char ch) {
			if (char.IsDigit(ch)) {
				return true;
			}

			switch (ch) {
				case 'a': case 'b': case 'c': case 'd': case 'e': case 'f':
				case 'A': case 'B': case 'C': case 'D': case 'E': case 'F':
					return true;
			}
			return false;
		}

		public static byte HexToBin(this char ch) {
			return ch switch {
				var x when x >= '0' && x <= '9' => (byte)(x - '0'),
				var x when x >= 'a' && x <= 'f' => (byte)(x - 'a' + 10),
				var x when x >= 'A' && x <= 'F' => (byte)(x - 'A' + 10),
				_ => throw new ArgumentException($"Invalid char {ch}")
			};
		}
	}
}
