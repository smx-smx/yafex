using log4net;
using Smx.Yafex.FileFormats.Epk;
using Smx.Yafex.Support;
using System;
using System.Text;

namespace Smx.Yafex.FileFormats.EpkV3
{
	public enum Epk3Variant
	{
		UNKNOWN,
		OLD,
		NEW
	}

	public class Epk3NewDetector : EpkDetector, IFormatDetector
	{
		private static readonly ILog log = LogManager.GetLogger(nameof(Epk3NewDetector));

		public Epk3NewDetector(Config config) : base(config) {
		}

		private static bool IsPlainHeader(EPK_V3_NEW_HEADER hdr) {
			return hdr.EpkMagic == Epk3Extractor.EPK3_MAGIC;
		}

		private bool IsPlainHeaderData(ReadOnlySpan<byte> data) {
			EPK_V3_NEW_HEADER hdr = data.ReadStruct<EPK_V3_NEW_HEADER>();
			return IsPlainHeader(hdr);
		}

		private bool ValidateEpk3Header(ReadOnlySpan<byte> data) {
			var bytes = data.Slice(0, 4).ToArray();
			return Encoding.ASCII.GetString(bytes) == Epk3Extractor.EPK3_MAGIC;
		}

		public DetectionResult Detect(IDataSource source) {
			var data = source.Data.ToReadOnlySpan();

			int confidence = 0;

			var epk3 = data.ReadStruct<EPK_V3_NEW_STRUCTURE>();
			if (IsEpkVersionString(epk3.head.platformVersion)) confidence += 40;
			if (IsEpkVersionString(epk3.head.sdkVersion)) confidence += 40;

			EPK_V3_NEW_HEADER header = epk3.head.epkHeader;
			if (!IsPlainHeader(header)) {
				var headBytes = EPK_V3_NEW_STRUCTURE.GetHead(data);
				var hdrBytes = EPK_V3_NEW_HEAD_STRUCTURE.GetHeader(headBytes);
				var decryptor = serviceFactory.CreateEpkDecryptor(hdrBytes, ValidateEpk3Header);
				if(decryptor != null) {
					var decrypted = decryptor.Decrypt(hdrBytes).ReadStruct<EPK_V3_NEW_HEADER>();
					decrypted.ToString();
				} else {
					if(confidence > 40) {
						log.Info("This could be a valid EPK2, but there's no matching AES key");
					}
					confidence = 0;
				}
			}

			return new DetectionResult(confidence, null);
		}
	}
}