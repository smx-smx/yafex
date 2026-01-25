using Yafex.Support;
using System;
using Yafex.Fuse;

namespace Yafex.FileFormats.Nfwb
{
    public class NfwbAddon : IFormatAddon
    {
        public IFormatDetector CreateDetector(Config config)
        {
            return new NfwbDetector(config);
        }

        public IFormatExtractor CreateExtractor(Config config, DetectionResult result)
        {
            return new NfwbExtractor(config, result);
        }

        public IVfsNode CreateVfsNode(IDataSource ds)
        {
            throw new NotImplementedException();
        }
    }
}