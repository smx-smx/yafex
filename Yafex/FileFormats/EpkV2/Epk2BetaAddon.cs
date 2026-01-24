using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Yafex.Fuse;
using Yafex.Support;

namespace Yafex.FileFormats.EpkV2
{
    class Epk2BetaAddon : IFormatAddon
    {
        public IFormatDetector CreateDetector(Config config)
        {
            return new Epk2BetaDetector(config);
        }

        public IFormatExtractor CreateExtractor(Config config, DetectionResult result)
        {
            return new Epk2BetaExtractor(config, result);
        }

        public IVfsNode CreateVfsNode(IDataSource ds)
        {
            return new YafexFile(ds, Helpers.OctalLiteral(0444));
        }
    }
}
