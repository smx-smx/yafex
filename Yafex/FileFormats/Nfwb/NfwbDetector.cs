using Yafex.Support;
using System.Text;

namespace Yafex.FileFormats.Nfwb
{
    public class NfwbDetector : IFormatDetector
    {
        public DetectionResult Detect(IDataSource source)
        {
            if(source.Data.Length > 192 &&
                source.Data.Slice(0, 4)
                    .ToArray()
                    .AsString(Encoding.ASCII) == "NFWB"
            )
            {
                return new DetectionResult(90, null);
            }
            return new DetectionResult(0, null);
        }
    }
}