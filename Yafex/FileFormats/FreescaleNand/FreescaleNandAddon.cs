using Smx.Yafex.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Smx.Yafex.FileFormats.FreescaleNand
{
	public class FreescaleNandAddon : IFormatAddon
	{
		public IFormatDetector CreateDetector(Config config) {
			return new FreescaleNandDetector();
		}

		public IFormatExtractor CreateExtractor(Config config, DetectionResult result) {
			return new FreescaleNandExtractor();
		}
	}
}
