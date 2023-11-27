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
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Markup;

namespace Yafex
{
	public class KeyEntry
	{
		public CipherAlgorithmType keyAlgo;
		public CipherMode keyMode;
		public byte[] key;
		public byte[] iv;
		public string comment;
	}

	public class KeyBundle : IDisposable
	{
		private readonly IEnumerable<KeyEntry> keys;
		private readonly StreamReader sr;

		public IEnumerable<KeyEntry> GetKeysEnumerable() => keys;

		private bool disposed = false;

		// aes-256
		private const int MAX_KEYSIZE = 64;

		private static byte[] ReadPiece(StreamReader sr, out char lastCh) {
			Memory<byte> bytes = new Memory<byte>(new byte[MAX_KEYSIZE]);
			lastCh = '\0';

			int i;
			for (i = 0; !sr.EndOfStream && i < MAX_KEYSIZE; i++) {
				char nibHigh = (char)sr.Read();
				lastCh = nibHigh;
				if (!nibHigh.IsHexDigit()) break;

				char nibLow = (char)sr.Read();
				lastCh = nibLow;
				if (!nibLow.IsHexDigit()) break;
				
				bytes.Span[i] = (byte)(nibHigh.HexToBin() << 4 | nibLow.HexToBin());
			}

			return bytes.Slice(0, i).ToArray();
		}

		private const char COMMENT_LITERAL = '#';

		private IEnumerable<KeyEntry> ReadKeys(StreamReader sr) {
			while (!sr.EndOfStream) {
				char lastCh;

                byte[] key;
                do
                {
                    key = ReadPiece(sr, out lastCh);
                } while (key.Length == 0 && !sr.EndOfStream && lastCh != COMMENT_LITERAL);

				if(lastCh == COMMENT_LITERAL)
				{
					sr.ReadLine();
					continue;
				}

                if (key.Length == 0 && sr.EndOfStream) break;

				byte[] iv = null;
				if (lastCh == ',') {
					iv = ReadPiece(sr, out _);

					if (iv.Length != key.Length) {
						throw new InvalidDataException($"IV length {iv.Length} != Key length {key.Length}");
					}
				}

				CipherAlgorithmType algo = key.Length switch
				{
					16 => CipherAlgorithmType.Aes128,
					32 => CipherAlgorithmType.Aes256,
					_ => throw new NotSupportedException($"Key length {key.Length} is not supported")
				};

				CipherMode mode = iv switch
				{
					null => CipherMode.ECB,
					_ => CipherMode.CBC
				};

				string comment = sr.ReadLine();

				KeyEntry entry = new KeyEntry() {
					key = key,
					iv = iv,
					comment = comment,
					keyAlgo = algo,
					keyMode = mode
				};
				yield return entry;
			}

			Dispose();
		}

		public void Dispose() {
			if (this.disposed) {
				return;
			}
			sr.Close();
			disposed = true;
		}

		public KeyBundle(string filePath) {
			this.sr = new StreamReader(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read));
			this.keys = new CachedEnumerable<KeyEntry>(ReadKeys(sr));
		}

		public KeyBundle(string content, Encoding encoding) {
			this.sr = new StreamReader(new MemoryStream(encoding.GetBytes(content)));
			this.keys = new CachedEnumerable<KeyEntry>(ReadKeys(sr));	
		}
	}
}
