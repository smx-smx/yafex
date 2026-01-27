using System;
using Yafex.Fuse;

namespace Yafex.FileFormats.Nfwb
{
    public class NfwbAddon : IFormatAddon
    {
        public FileFormat FileFormat => FileFormat.Nfwb;

        public IFormatDetector CreateDetector()
        {
            return new NfwbDetector();
        }

        public IFormatExtractor CreateExtractor(DetectionResult result)
        {
            return new NfwbExtractor(result);
        }

        public IVfsNode CreateVfsNode(IDataSource ds)
        {
            throw new NotImplementedException();
        }
    }
}