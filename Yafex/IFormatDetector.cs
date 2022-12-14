using Yafex.Support;
using System;
using System.Collections.Generic;
using System.Text;

namespace Yafex
{
	public interface IFormatDetector
	{
		public DetectionResult Detect(IDataSource source);
	}
}
