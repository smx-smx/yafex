using System.Collections.Generic;

using Yafex.Fuse;
using Yafex.Support;

namespace Yafex.FileFormats.EpkV2
{
    public class Epk2BetaAddon : FormatAddon<Epk2DetectionResult<EPK_V2_BETA_HEADER>>
    {
        public override FileFormat FileFormat => FileFormat.EpkV2Beta;

        private readonly KeysRepository _keysRepo;

        public Epk2BetaAddon(KeysRepository keysRepo)
        {
            _keysRepo = keysRepo;
        }

        public override IFormatDetector CreateDetector(IDictionary<string, string> args)
        {
            return new Epk2BetaDetector(_keysRepo);
        }

        protected override bool IsUsable(Epk2DetectionResult<EPK_V2_BETA_HEADER> result) => result.Context != null;

        protected override IFormatExtractor CreateExtractor(Epk2DetectionResult<EPK_V2_BETA_HEADER> result)
        {
            return new Epk2BetaExtractor(result);
        }

        protected override IFormatLister? CreateLister(Epk2DetectionResult<EPK_V2_BETA_HEADER> typed)
        {
            return new Epk2Lister<EPK_V2_BETA_HEADER>(typed);
        }

        public override YafexVfs? CreateVfs(IDataSource source, DetectionResult result)
        {
            if (source is not MFile input) return null;
            return EpkVfs.TryCreate(input, _keysRepo, out var vfs) ? vfs : null;
        }
    }
}
