using Yafex.Support;
using System;
using System.Collections.Generic;
using System.Text;

namespace Yafex
{
	public interface IFormatAddon
	{
		IFormatExtractor CreateExtractor(Config config, DetectionResult result);
		IFormatDetector CreateDetector(Config config);
	}
}
