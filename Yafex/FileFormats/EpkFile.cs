using Smx.Yafex.Support;
using Smx.Yafex.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Smx.Yafex.FileFormats
{
	enum EpkBuildType
	{
		Release,
		Debug,
		Test,
		Unknown
	}

	public enum EpkInternalFileType
	{
		Epk,
		EpkV2,
		EpkV3,
		EpkV3New,
		PakV2,
		Raw
	}

	public abstract class EpkFile
	{
		public const int SIGNATURE_SIZE = 0x80;

		protected EpkFile(Config config) {
			
		}

		private RSA FindPublicKey(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature/*, out int signedSize*/) {
			foreach(string file in Directory.EnumerateFiles("foo")) {
				byte[] key = File.ReadAllBytes(file);

				RSA rsa = RSA.Create();
				rsa.ImportRSAPublicKey(key, out int bytesRead);

				//for(int curSize = data.Length; curSize > 0; curSize--){
				if (rsa.VerifyHash(data, signature, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1)) {
					//signedSize = curSize;
					return rsa;
				}
				//}
			}

			//signedSize = 0;
			return null;
		}
	}
}
