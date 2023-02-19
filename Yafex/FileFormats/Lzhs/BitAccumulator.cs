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
﻿namespace Yafex.FileFormats.Lzhs
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
