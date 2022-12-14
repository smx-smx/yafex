using Yafex.Support;
using System.Collections.Generic;
using System.Linq;

namespace Yafex.FileFormats.Lzhs
{
	internal class LzhsExtractor : IFormatExtractor
	{
		private Config config;
		private DetectionResult result;

		public LzhsExtractor(Config config, DetectionResult result) {
			this.config = config;
			this.result = result;
		}

		public IEnumerable<IDataSource> Extract(IDataSource source) {
			return Enumerable.Empty<IDataSource>();
		}
	}
}