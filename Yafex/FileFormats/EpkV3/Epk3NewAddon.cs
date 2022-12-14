using Yafex.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yafex.FileFormats.EpkV3
{
	public class Epk3NewAddon : IFormatAddon
	{
		public IFormatDetector CreateDetector(Config config) {
			return new Epk3NewDetector(config);
		}

		public IFormatExtractor CreateExtractor(Config config, DetectionResult result) {
			throw new NotImplementedException();
		}
	}
}
