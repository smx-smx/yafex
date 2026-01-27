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
using Yafex.Support;
using System;
using System.IO;
using System.Security.Cryptography;
using log4net;

namespace Yafex.FileFormats.Epk
{

	public class EpkDecryptionService
	{
		private Aes aes;

		public EpkDecryptionService(Aes aes) {
			this.aes = aes;
		}

		public Memory<byte> Decrypt(ReadOnlySpan<byte> data) {
			ICryptoTransform decryptor = aes.CreateDecryptor();

			MemoryStream outStream = new MemoryStream(data.Length);

			CryptoStream cs = new CryptoStream(outStream, decryptor, CryptoStreamMode.Write);
			cs.Write(data);
			cs.Flush();

			if (!outStream.TryGetBuffer(out ArraySegment<byte> buf)) {
				return null;
			}
			return buf;
		}

	}

	public class EpkServicesFactory
	{
		private readonly Config config;
		private readonly KeyBundle KeyFile;

		private static readonly ILog logger = LogManager.GetLogger(typeof(EpkServicesFactory));

		public EpkServicesFactory(Config config) {
			this.config = config;

			// $FIXME: should be made global
			var keyFilePath = Path.Combine(config.ConfigDir, "secrets.json");
			logger.InfoFormat("Using AES key file: {0}", keyFilePath);

			if (File.Exists(keyFilePath)) {
				this.KeyFile = new KeyBundle(keyFilePath);
			}
		}

		public EpkDecryptionService? CreateEpkDecryptor(ReadOnlySpan<byte> data, CryptoResultChecker validator) {
			if (KeyFile == null) {
				return null;
			}

			var keyFinder = new AesKeyFinder(this.KeyFile, "lg-epk-keys");
			if(!keyFinder.FindAesKey(data, validator, out var result))
			{
				return null;
			}

            var keyEntry = result.Value.Key;
			
			var aes = Aes.Create();
            aes.BlockSize = 128;
            aes.KeySize = keyEntry.key.Length * 8;
            aes.Key = keyEntry.key;
			if (keyEntry.keyMode == CipherMode.CBC)
			{
				aes.IV = keyEntry.iv;
			}
			aes.Mode = keyEntry.keyMode;
			aes.Padding = PaddingMode.None;
            return new EpkDecryptionService(aes);
		}
	}

	public class EpkServices
	{
		public EpkDecryptionService? Decryptor { get; set; }
	}
}
