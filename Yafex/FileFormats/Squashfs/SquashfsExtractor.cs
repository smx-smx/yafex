using FirmexSharp.Cygwin;
using Smx.Yafex.Support;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Smx.Yafex.FileFormats.Squashfs
{
	public class SquashfsExtractor : IFormatExtractor
	{
		private readonly Config config;

		public SquashfsExtractor(Config config, DetectionResult result) {
			this.config = config;
		}

		public IEnumerable<IDataSource> Extract(IDataSource source) {
			if (!(source is MFile)) {
				throw new NotSupportedException();
			}

			var squashfs = new SquashfsNative();

			string cygSource = Cygwin.ToPosixPath(source.Directory);
			string dirName = source.Directory + ".unsquashfs";

			string cygDest = Cygwin.ToPosixPath(Path.Combine(config.DestDir, dirName));
			squashfs.ExtractSquashfs(cygSource, cygDest);

			return Enumerable.Empty<IDataSource>();
		}
	}
}
