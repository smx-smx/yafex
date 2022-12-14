using log4net;
using Yafex.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yafex.FileFormats.Partinfo
{
	public class PartinfoDetector : IFormatDetector
	{
		private static ILog log = LogManager.GetLogger(nameof(PartinfoDetector));

		private PartinfoType? ToPartinfoType(uint magic) {
			switch (magic) {
				case PartinfoV2.PartmapInfo.MAGIC: return PartinfoType.PartinfoV2;
				case PartinfoV1.PartmapInfo.MAGIC: return PartinfoType.PartinfoV1;
				case MtdInfo.PartmapInfo.MAGIC: return PartinfoType.MtdInfo;
				default:
					uint d = (magic >> 0) & 0xFF;
					uint m = (magic >> 8) & 0xFF;
					uint y = (magic >> 16) & 0xFFFF;
					if (y >= 2008 && m <= 12 && d <= 31) {
						// this might be an unsupported partinfo format, but it's too generic to assume so
						// just log
						log.Warn($"Potential unknown partinfo magic 0x{magic:X}");
						return PartinfoType.Unknown;
					}
					return null;
			}
		}

		public DetectionResult Detect(IDataSource source) {
			uint magic = source.Data.Slice(0, 4).Cast<uint>()[0];

			int confidence = 0;

			PartinfoType? partinfoType = ToPartinfoType(magic);
			PartinfoContext? ctx = null;

			switch (partinfoType) {
				case PartinfoType.MtdInfo:
				case PartinfoType.PartinfoV1:
				case PartinfoType.PartinfoV2:
					confidence += 30;
					ctx = new PartinfoContext() {
						PartinfoType = (PartinfoType)partinfoType
					};
					break;
			}
			return new DetectionResult(confidence, ctx);
		}
	}
}
