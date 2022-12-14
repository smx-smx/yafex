using System;
using System.Runtime.Serialization;
using System.Text;

namespace Yafex.FileFormats.MStarPkg
{
	public class MStarPkgDetector : IFormatDetector
	{
		public const int MBOOT_SCRIPT_SIZE = 0x4000;

		public MStarPkgDetector() {
		}

		public DetectionResult Detect(IDataSource source) {
			if(source.Data.Length < MBOOT_SCRIPT_SIZE) {
				return new DetectionResult(0, null);
			}

			var script = Encoding.ASCII.GetString(
				source.Data.Span.Slice(0, MBOOT_SCRIPT_SIZE).ToArray()
			);

			if(script.Contains("setenv MstarUpgrade_complete")) {
				return new DetectionResult(100, null);
			}

			return new DetectionResult(0, null);
		}
	}
}