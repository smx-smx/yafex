using Yafex.Support;
using System.Text;

namespace Yafex.FileFormats.FreescaleNand
{
	public class FreescaleNandDetector : IFormatDetector
	{
		public DetectionResult Detect(IDataSource source) {
			if(source.Data.Length > 0x20 &&
				source.Data.Span.Slice(0x10, 4)
				.ToArray()
				.AsString(Encoding.ASCII) == "FCB "
			) {
				return new DetectionResult(100, null);
			}
			return new DetectionResult(0, null);
		}
	}
}