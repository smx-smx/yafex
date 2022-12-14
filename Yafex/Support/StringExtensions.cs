using System;
using System.Collections.Generic;
using System.Text;

namespace Yafex.Support
{
	public static class StringExtensions
	{
		public static string TakeUntilChar(this string str, char ch) {
			int pos = str.IndexOf(ch);
			return str.Substring(0, pos);
		}
	}
}
