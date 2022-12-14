using Yafex.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yafex.FileFormats.MStarPkg
{
	class MStarPkgAddon : IFormatAddon
	{
		public IFormatDetector CreateDetector(Config config) {
			return new MStarPkgDetector();
		}

		public IFormatExtractor CreateExtractor(Config config, DetectionResult result) {
			return new MStarPkgExtractor(config);
		}
	}
}
