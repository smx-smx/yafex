using Yafex.Support;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Yafex.FileFormats.Squashfs
{

	public class SquashfsDetector : IFormatDetector
	{
		private Config config;

		public SquashfsDetector(Config config) {
			this.config = config;
		}

		public DetectionResult Detect(IDataSource source) {
			if (!(source is MFile)) {
				throw new NotSupportedException();
			}

			var squashfs = new SquashfsNative();

			string cygwinPath = Cygwin.Cygwin.ToPosixPath(source.Directory);

			int confidence = 0;

			if (squashfs.IsSquashfs(cygwinPath)) {
				confidence += 100;
			}

			return new DetectionResult(confidence, null);
		}
	}
}