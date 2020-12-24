using Smx.Yafex.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Smx.Yafex.FileFormats.LzhsFs
{
	public class LzhsFsAddon : IFormatAddon
	{
		public IFormatDetector CreateDetector(Config config) {
			return new LzhsFsDetector(config);
		}

		public IFormatExtractor CreateExtractor(Config config, DetectionResult result) {
			return new LzhsFsExtractor(config, result);
		}
	}
}
