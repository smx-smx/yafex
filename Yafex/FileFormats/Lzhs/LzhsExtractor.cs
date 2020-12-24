using Smx.Yafex.Support;
using System.Collections.Generic;

namespace Smx.Yafex.FileFormats.Lzhs
{
	internal class LzhsExtractor : IFormatExtractor
	{
		private Config config;
		private DetectionResult result;

		public LzhsExtractor(Config config, DetectionResult result) {
			this.config = config;
			this.result = result;
		}

		public IList<IArtifact> Extract(IDataSource source) {
			return new List<IArtifact>();
		}
	}
}