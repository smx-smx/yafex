using System.Linq;
using System.Runtime.CompilerServices;

using Smx.SharpIO.Extensions;

using Yafex;
using Yafex.Support;

public record SddlSecDetectionResult(int Confidence, SddlSecContext? Context) : DetectionResult(Confidence);

namespace Yafex.FileFormats.SddlSec
{
    public class SddlSecDetector : IFormatDetector
    {
        private readonly SddlSecContext _ctx;
        public SddlSecDetector()
        {
            _ctx = new SddlSecContext();
        }

        public DetectionResult Detect(IDataSource source)
        {
            var data = source.Data;
            var headerSize = Unsafe.SizeOf<SddlSecHeader>();

            if(data.Length > headerSize)
            {
                var headerBytes = data.Slice(0, headerSize);
                var decipheredHeaderBytes = SddlSecExtractor.Decipher(headerBytes);
                var hdr = decipheredHeaderBytes.Cast<byte, SddlSecHeader>().Span[0];

                if (hdr.HeaderMagic.SequenceEqual(SddlSecHeader.SDDL_SEC_HEADER_MAGIC))
                {
                    _ctx.Header = hdr;
                    return new SddlSecDetectionResult(100, _ctx);
                }
            }
            return new SddlSecDetectionResult(0, null);
        }
    }
}