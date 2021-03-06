using log4net;
using Smx.Yafex.FileFormats.Epk;
using Smx.Yafex.Support;
using System;

namespace Smx.Yafex.FileFormats.EpkV3
{
	public class Epk3OldDetector : EpkDetector, IFormatDetector
	{
		private static readonly ILog log = LogManager.GetLogger(nameof(Epk3OldDetector));

		private Config config;

		public Epk3OldDetector(Config config) : base(config) {
			this.config = config;
		}

		private static bool IsPlainHeader(EPK_V3_HEADER hdr) {
			return hdr.EpkMagic == Epk3Extractor.EPK3_MAGIC;
		}

		private bool IsPlainHeaderData(ReadOnlySpan<byte> data) {
			EPK_V3_HEADER hdr = data.ReadStruct<EPK_V3_HEADER>();
			return IsPlainHeader(hdr);
		}

		private Epk3Variant DetectEpk3Type(ReadOnlySpan<byte> data) {
			var head_old = data.ReadStruct<EPK_V3_HEAD_STRUCTURE>();

			if (IsEpkVersionString(head_old.platformVersion)
			 && IsEpkVersionString(head_old.sdkVersion)
			 ) {
				return Epk3Variant.OLD;
			}

			var head_new = data.ReadStruct<EPK_V3_NEW_HEAD_STRUCTURE>();
			if(IsEpkVersionString(head_new.platformVersion)
			&& IsEpkVersionString(head_new.sdkVersion)
			){
				return Epk3Variant.NEW;
			}

			return Epk3Variant.UNKNOWN;
		}

		private Epk3Context<T> CreateContext<T>(T header) where T : struct {
			return new Epk3Context<T>(
				serviceFactory,
				new EpkServices(),
				header
			);
		}

		public DetectionResult Detect(IDataSource source) {
			var data = source.Data.ToReadOnlySpan();

			int confidence = 0;

			var type = DetectEpk3Type(data);
			if(type == Epk3Variant.UNKNOWN) {
				return new DetectionResult(0, null);
			}

			confidence += 50;

			throw new NotImplementedException();
		}
	}
}