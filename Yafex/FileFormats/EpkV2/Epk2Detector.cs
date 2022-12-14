using log4net;
using Yafex.FileFormats.Epk;
using Yafex.Support;
using System;

namespace Yafex.FileFormats.EpkV2
{
	public class Epk2Detector : EpkDetector, IFormatDetector
	{
		private static readonly ILog log = LogManager.GetLogger(nameof(Epk2Detector));

		public Epk2Detector(Config config) : base(config) {
		}

		private static bool IsPlainHeader(EPK_V2_HEADER hdr) {
			return hdr.EpkMagic == Epk2Extractor.EPK2_MAGIC;
		}

		protected bool IsPlainHeaderData(ReadOnlySpan<byte> data) {
			EPK_V2_HEADER hdr = data.ReadStruct<EPK_V2_HEADER>();
			return IsPlainHeader(hdr);
		}

		private Epk2Context CreateContext(EPK_V2_HEADER? header) {
			return new Epk2Context(
				serviceFactory,
				new EpkServices(),
				header.HasValue ? header.Value : default
			);
		}

		public DetectionResult Detect(IDataSource source) {
			var data = source.Data.ToReadOnlySpan();

			EPK_V2_STRUCTURE epk = data.ReadStruct<EPK_V2_STRUCTURE>();

			int confidence = 0;
			
			if (IsEpkVersionString(epk.platformVersion)) confidence += 40;
			if (IsEpkVersionString(epk.sdkVersion)) confidence += 40;

			EPK_V2_HEADER header = epk.epkHeader;
			
			Epk2Context? ctx = null;
			if (!IsPlainHeader(header)) {
				var hdrBytes = EPK_V2_STRUCTURE.GetHeader(data);
				var decryptor = serviceFactory.CreateEpkDecryptor(hdrBytes, ValidateEpkHeader);
				if (decryptor != null) {
					header = decryptor.Decrypt(hdrBytes).ReadStruct<EPK_V2_HEADER>();
					ctx = CreateContext(header);
					ctx.Services.Decryptor = decryptor;
				} else {
					if (confidence > 40) {
						log.Info("This could be a valid EPK2, but there's no matching AES key");
					}
					confidence = 0;
				}
			}

			if (IsPlainHeader(header)) {
				confidence += 100;
				ctx = CreateContext(header);
			}

			return new DetectionResult(confidence, ctx);
		}
	}
}
