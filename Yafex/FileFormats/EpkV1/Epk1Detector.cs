using Smx.Yafex.Support;
using System;
using System.Text;

namespace Smx.Yafex.FileFormats.EpkV1
{
	public enum Epk1Type
	{
		BigEndian,
		Old,
		New
	}

	public class Epk1Detector : IFormatDetector
	{
		private Config config;

		public Epk1Detector(Config config) {
			this.config = config;
		}

		private Epk1Type GetEpkType(Epk1Header hdr) {
			// pakCount is always at the same offset for all 3 structures
			if(hdr.pakCount >> 8 != 0) {
				return Epk1Type.BigEndian;
			}

			if(hdr.pakCount < 21) {
				return Epk1Type.Old;
			}

			return Epk1Type.New;
		}

		public DetectionResult Detect(IDataSource source) {
			var data = source.Data;
			var hdr = data.ReadStruct<Epk1Header>();
			
			int confidence = 0;
			if(hdr.EpakMagic == "epak") {
				confidence += 100;
			}

			var epkType = GetEpkType(hdr);
			return new DetectionResult(confidence, epkType);
		}
	}
}