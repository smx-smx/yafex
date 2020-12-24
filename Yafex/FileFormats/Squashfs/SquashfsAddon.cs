using Smx.Yafex.Support;
using System;
using System.Collections.Generic;
using System.Text;

namespace Smx.Yafex.FileFormats.Squashfs
{
	public class SquashfsAddon : IFormatAddon
	{
		public IFormatDetector CreateDetector(Config config) {
			return new SquashfsDetector(config);
		}

		public IFormatExtractor CreateExtractor(Config config, DetectionResult result) {
			return new SquashfsExtractor(config, result);
		}
	}
}
