#region License
/*
 * Copyright (c) 2023 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Yafex
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
