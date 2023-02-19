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
﻿using Yafex.Support;
using Yafex.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Yafex.FileFormats.Epk
{

	public class EpkDecryptionService
	{
		private Aes aes;

		public EpkDecryptionService(Aes aes) {
			this.aes = aes;
		}

		public Span<byte> Decrypt(ReadOnlySpan<byte> data) {
			ICryptoTransform decryptor = aes.CreateDecryptor();

			MemoryStream outStream = new MemoryStream(data.Length);

			CryptoStream cs = new CryptoStream(outStream, decryptor, CryptoStreamMode.Write);
			cs.Write(data);
			cs.Flush();

			if (!outStream.TryGetBuffer(out ArraySegment<byte> buf)) {
				return null;
			}
			return buf.AsSpan();
		}

	}

	public class EpkServicesFactory
	{
		private readonly Config config;
		private readonly KeyFile KeyFile;

		public EpkServicesFactory(Config config) {
			this.config = config;

			var keyFilePath = Path.Combine(config.ConfigDir, "AES.key");
			if (File.Exists(keyFilePath)) {
				this.KeyFile = new KeyFile(keyFilePath);
			}
		}

		public EpkDecryptionService? CreateEpkDecryptor(ReadOnlySpan<byte> data, ValidatorDelegate validator) {
			if (KeyFile == null) {
				return null;
			}

			Aes key = KeyFile.FindAesKey(data, validator);
			if (key == null) {
				return null;
			}

			return new EpkDecryptionService(key);
		}
	}

	public class EpkServices
	{
		public EpkDecryptionService? Decryptor { get; set; }
	}
}
