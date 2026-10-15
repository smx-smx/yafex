#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
using System.Collections.Generic;

using Yafex.Fuse;
using Yafex.Support;

namespace Yafex.FileFormats.EpkV2
{
    public class Epk2Addon : FormatAddon<Epk2DetectionResult<EPK_V2_HEADER>>
    {
        public override FileFormat FileFormat => FileFormat.EpkV2;

        private readonly KeysRepository _keysRepo;

        public Epk2Addon(KeysRepository keysRepo)
        {
            _keysRepo = keysRepo;
        }

        public override IFormatDetector CreateDetector(IDictionary<string, string> args)
        {
            return new Epk2Detector(_keysRepo);
        }

        protected override bool IsUsable(Epk2DetectionResult<EPK_V2_HEADER> result) => result.Context != null;

        protected override IFormatExtractor CreateExtractor(Epk2DetectionResult<EPK_V2_HEADER> result)
        {
            return new Epk2Extractor(result);
        }

        protected override IFormatLister? CreateLister(Epk2DetectionResult<EPK_V2_HEADER> typed)
        {
            return new Epk2Lister<EPK_V2_HEADER>(typed);
        }

        public override YafexVfs? CreateVfs(IDataSource input, DetectionResult result)
        {
            return EpkVfs.TryCreate(input, _keysRepo, out var vfs) ? vfs : null;
        }
    }
}
