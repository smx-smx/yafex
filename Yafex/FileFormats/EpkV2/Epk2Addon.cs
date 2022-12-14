using Yafex.Support;

namespace Yafex.FileFormats.EpkV2
{
	class Epk2Addon : IFormatAddon
	{
		public IFormatDetector CreateDetector(Config config) {
			return new Epk2Detector(config);
		}

		public IFormatExtractor CreateExtractor(Config config, DetectionResult result) {
			return new Epk2Extractor(config, result);
		}
	}
}
