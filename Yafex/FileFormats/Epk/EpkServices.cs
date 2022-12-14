using Yafex.Support;
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
