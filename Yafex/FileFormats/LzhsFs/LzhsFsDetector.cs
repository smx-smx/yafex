using Yafex.FileFormats.Lzhs;
using Yafex.Support;
using System;
using System.IO;
using System.Linq;

namespace Yafex.FileFormats.LzhsFs
{
	internal class LzhsFsDetector : IFormatDetector
	{
		private Config config;

		public LzhsFsDetector(Config config) {
			this.config = config;
		}

		public DetectionResult Detect(IDataSource source) {
			if(source.Data.Length < LzhsFsReader.UNCOMPRESSED_HEADING_SIZE + 32) {
				return new DetectionResult(0, null);
			}

			try {
				LzhsHeader firstHdr = new LzhsHeader(source.Data.Span.Slice(LzhsFsReader.UNCOMPRESSED_HEADING_SIZE, 16));
				if (firstHdr.checksum != 1) throw new InvalidDataException();

				var innerData = source.Data.Slice(LzhsFsReader.UNCOMPRESSED_HEADING_SIZE + 16);
				var dec = new LzhsDecoder(innerData);
				dec.AsEnumerable().ToArray();
				bool result = dec.VerifyChecksum();
				Console.WriteLine($"{firstHdr.checksum}: " + (result ? "PASS" : "FAIL"));

				return result
					? new DetectionResult(80, null)
					: new DetectionResult(0, null);

			} catch (InvalidDataException) {
				return new DetectionResult(0, null);
			}
		}
	}
}