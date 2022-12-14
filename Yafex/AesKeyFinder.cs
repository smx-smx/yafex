using System;
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
