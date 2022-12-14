using Yafex.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yafex.FileFormats.Lzhs
{
	class LzhsAddon : IFormatAddon
	{
		public IFormatDetector CreateDetector(Config config) {
			return new LzhsDetector(config);
		}

		public IFormatExtractor CreateExtractor(Config config, DetectionResult result) {
			return new LzhsExtractor(config, result);
		}
	}
}
