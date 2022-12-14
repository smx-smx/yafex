using Yafex.Support;
using System.Text;

namespace Yafex.FileFormats.Xex
{
    public class XexDetector : IFormatDetector
    {
        public DetectionResult Detect(IDataSource source)
        {
            if(source.Data.Length > 200 &&
                source.Data.Slice(0, 4)
                    .ToArray()
                    .AsString(Encoding.ASCII) == "XEX2"
            )
            {
                return new DetectionResult(50, null);
            }
            return new DetectionResult(0, null);
        }
    }
}