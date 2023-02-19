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
using System.IO;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;

namespace Yafex
{
	public struct KeyFinderResult
	{
		public KeyEntry Key;
		public Memory<byte> Data;
	}

	public class AesKeyFinder
	{
		public delegate bool CryptoResultChecker(Span<byte> data);

		private readonly KeyBundle bundle;

		public AesKeyFinder(KeyBundle bundle) {
			this.bundle = bundle;
		}

		private Span<byte> TestAesKey(ReadOnlySpan<byte> data, KeyEntry key, CryptoResultChecker validator) {
			var aes = Aes.Create();
			var decryptor = aes.CreateDecryptor();

			var outStream = new MemoryStream(data.Length);

			var cs = new CryptoStream(outStream, decryptor, CryptoStreamMode.Write);
			cs.Write(data);
			cs.Flush();

			if (!outStream.TryGetBuffer(out ArraySegment<byte> buf)) {
				return null;
			}

			var span = buf.AsSpan();
			if (validator(span)) {
				return span;
			}

			return null;
		}

		public bool FindAesKey(ReadOnlySpan<byte> data, CryptoResultChecker checker, out KeyFinderResult? result) {
			var keys = bundle.GetKeysEnumerable().Where(k => k.keyAlgo == CipherAlgorithmType.Aes);
			foreach (var k in keys) {
				var decrypted = TestAesKey(data, k, checker);
				if (decrypted != null) {
					result = new KeyFinderResult {
						Key = k,
						Data = decrypted.ToArray()
					};
					return true;
				}
			}

			result = null;
			return false;
		}
	}
}
