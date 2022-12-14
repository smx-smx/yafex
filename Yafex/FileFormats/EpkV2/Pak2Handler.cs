using Yafex.FileFormats.Epk;
using Yafex.Support;
using System;
using System.Runtime.InteropServices;

namespace Yafex.FileFormats.EpkV2
{
	class Pak2DetectionResult
	{
		public bool WasDecrypted { get; set; }
		public PAK_V2_HEADER Header { get; set; }
	}

	class Pak2Handler : IFormatDetector
	{
		private readonly Epk2Context ctx;

		public Pak2Handler(Epk2Context ctx) {
			this.ctx = ctx;
		}

		private static bool IsPlainHeader(PAK_V2_HEADER header) {
			return header.PakMagic == PAK_V2_HEADER.PAK_MAGIC;
		}

		private static bool CheckPak2Magic(ReadOnlySpan<byte> data) {
			var hdr = data.ReadStruct<PAK_V2_HEADER>();
			return hdr.PakMagic == PAK_V2_HEADER.PAK_MAGIC;
		}

		private PAK_V2_HEADER DecryptIfNeeded(ReadOnlySpan<byte> data, out bool wasDecrypted) {
			var hdr = data.ReadStruct<PAK_V2_HEADER>();
			if (IsPlainHeader(hdr)) {
				wasDecrypted = false;
				return hdr;
			}

			this.ctx.EnsureDecryptor(data, CheckPak2Magic);
			data = ctx.Services.Decryptor!.Decrypt(data);
			wasDecrypted = true;
			return data.ReadStruct<PAK_V2_HEADER>();
		}

		public DetectionResult Detect(ReadOnlySpan<byte> data) {
			int confidence = 0;

			var pak2 = DecryptIfNeeded(data, out bool wasDecrypted);
			if (IsPlainHeader(pak2)) {
				confidence += 100;
			}

			var result = new Pak2DetectionResult() {
				Header = pak2,
				WasDecrypted = wasDecrypted
			};

			return new DetectionResult(confidence, result);
		}

		public DetectionResult Detect(IDataSource source) => Detect(source.Data.ToReadOnlySpan());
	}
}
