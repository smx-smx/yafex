using System.Collections.Generic;

using Yafex.Fuse;
using Yafex.Support;

namespace Yafex.FileFormats.EpkV2
{
    public class Epk2BetaAddon : IFormatAddon<Epk2DetectionResult<EPK_V2_BETA_HEADER>>
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

        public IFormatExtractor CreateExtractor(Epk2DetectionResult<EPK_V2_BETA_HEADER> result)
        {
            return new Epk2BetaExtractor(result);
        }

        public IFormatLister? CreateLister(DetectionResult result)
        {
            return result is Epk2DetectionResult<EPK_V2_BETA_HEADER> typed && typed.Context != null
                ? new Epk2Lister<EPK_V2_BETA_HEADER>(typed)
                : null;
        }

        public IVfsNode CreateVfsNode(IDataSource ds)
        {
            return new YafexFile(ds, Helpers.OctalLiteral(0444));
        }

        public YafexVfs? CreateVfs(IDataSource source, DetectionResult result)
        {
            if (source is not MFile input) return null;
            return EpkVfs.TryCreate(input, _keysRepo, out var vfs) ? vfs : null;
        }
    }
}
