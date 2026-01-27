using Yafex.FileFormats.Epk;
using Yafex.Support;

namespace Yafex.FileFormats.EpkV2
{
    public class Epk2BetaDetector : EpkDetector, IFormatDetector
    {
        public Epk2BetaDetector(KeysRepository keysRepo) : base(keysRepo)
        {
        }

        public DetectionResult Detect(IDataSource source)
        {
            var data = source.Data;
            var hdr = data.ReadStruct<EPK_V2_BETA_HEADER>();
            var confidence = 0;
            object? ctx = default;
            if (hdr.FileType == EpkDetector.EPAK_MAGIC && hdr.EpkMagic == EPK_V2_HEADER.EPK2_MAGIC)
            {
                confidence += 100;
                ctx = new Epk2BetaContext(serviceFactory, new EpkServices(), hdr);
            }
            return new DetectionResult(confidence, ctx);
        }
    }
}
