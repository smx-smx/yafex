using Smx.Yafex.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Smx.Yafex.FileFormats.Partinfo
{
	public class PartinfoAddon : IFormatAddon
	{
		public IFormatDetector CreateDetector(Config config) {
			return new PartinfoDetector();
		}

		public IFormatExtractor CreateExtractor(Config config, DetectionResult result) {
			return new PartinfoExtractor(config, result);
		}
	}
}
