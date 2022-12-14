using Yafex.Support;
using System;
using System.Collections.Generic;
using System.Text;

namespace Yafex.FileFormats.EpkV1
{
	class Epk1Addon : IFormatAddon
	{
		public IFormatDetector CreateDetector(Config config) {
			return new Epk1Detector(config);
		}

		public IFormatExtractor CreateExtractor(Config config, DetectionResult result) {
			return new Epk1Extractor(config, result);
		}
	}
}
