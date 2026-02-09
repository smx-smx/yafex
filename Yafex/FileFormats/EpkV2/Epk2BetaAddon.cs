using System.Collections.Generic;

using Yafex.Fuse;

namespace Yafex.FileFormats.EpkV2
{
    public class Epk2BetaAddon : IFormatAddon
    {
        public FileFormat FileFormat => FileFormat.EpkV2Beta;

        private readonly KeysRepository _keysRepo;

        public Epk2BetaAddon(KeysRepository keysRepo)
        {
            _keysRepo = keysRepo;
        }

        public IFormatDetector CreateDetector(IDictionary<string, string> args)
        {
            return new Epk2BetaDetector(_keysRepo);
        }

        public IFormatExtractor CreateExtractor(DetectionResult result)
        {
            return new Epk2BetaExtractor(result);
        }

        public IVfsNode CreateVfsNode(IDataSource ds)
        {
            return new YafexFile(ds, Helpers.OctalLiteral(0444));
        }
    }
}
