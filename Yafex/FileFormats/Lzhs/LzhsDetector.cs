using log4net;
using Smx.Yafex.Support;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Smx.Yafex.FileFormats.Lzhs
{
	public class LzhsDetector : IFormatDetector
	{
		private static readonly ILog log = LogManager.GetLogger(nameof(LzhsDetector));
		private Config config;

		public LzhsDetector(Config config) {
			this.config = config;
		}

		public DetectionResult Detect(IDataSource source) {
			var data = source.Data;

			LzhsHeader hdr;
			try {
				hdr = new LzhsHeader(data.ToReadOnlySpan());
			} catch (InvalidDataException) {
				return new DetectionResult(0, null);
			}

			var decoder = new LzhsDecoder(data);

			foreach (var item in decoder.AsEnumerable()) ;
			if (decoder.VerifyChecksum()) {
				return new DetectionResult(50, null);
			} else {
				return new DetectionResult(0, null);
			}
		}
	}
}