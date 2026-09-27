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
using System;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Yafex.Support;

namespace Yafex.FileFormats.LxBoot
{
    public class LxSecureBootExtractor : IFormatExtractor
    {
        private readonly LxSecureBootContext ctx;
        public LxSecureBootExtractor(LxSecureBootContext ctx)
        {
            this.ctx = ctx;
        }

        private static byte[] Slice(IDataSource source, long offset, long count)
        {
            if (offset < 0 || count < 0 || offset + count > source.Data.Length)
            {
                throw new InvalidDataException("LXSecureBoot slice out of range");
            }
            return source.Data.Slice(offset, count).ToArray();
        }

        public IEnumerable<IDataSource> Extract(IDataSource source)
        {
            if (ctx.SpbcVariant == SpbcVariant.Bare)
            {
                if (!LxSecureBootDetector.TryReadBareSecondStage(source.Data, out uint size))
                {
                    throw new InvalidDataException("bad bare LL header");
                }
                var baseName = source.Name ?? "lxboot";
                foreach (var ext in new[] { ".pak", ".bin", ".lr" })
                {
                    if (baseName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    {
                        baseName = baseName[..^ext.Length];
                        break;
                    }
                }
                // raw stage image: leaf, no further processing (it would
                // re-detect as itself)
                var artifact = ArtifactBuilder.Flat(source, baseName + ".bin", Slice(source, 0, size));
                artifact.Flags |= DataSourceFlags.Output;
                yield return artifact;
                yield break;
            }

            var data = source.Data;

            var firstOffset = ctx.SpbcDescriptors.First().LoadAddress & 0xFFFF;

            var secondStageOffset = ctx.SpbcSecondStageOffset;

            var isSecondStage = secondStageOffset != null;
            var firstEnd = isSecondStage
                ? secondStageOffset
                : (uint)data.Length;

            var first = ArtifactBuilder.Flat(source, "secureboot_0.bin",
                Slice(source, firstOffset, (long)(firstEnd - firstOffset)));
            first.Flags |= DataSourceFlags.Output;
            yield return first;

            if (isSecondStage)
            {
                var header = data.ReadStruct<LxBootHeader>((long)secondStageOffset);
                if (header.Magic != 0x4C4C5F42 && header.Magic != 0x425F4C4C)
                {
                    throw new InvalidDataException("bad LL second-stage magic");
                }
                if (header.Size < 16 || (long)secondStageOffset + header.Size > data.Length)
                {
                    throw new InvalidDataException("bad LL second-stage size");
                }
                var second = ArtifactBuilder.Flat(source, "secureboot_1.bin",
                    Slice(source, (long)secondStageOffset, header.Size));
                second.Flags |= DataSourceFlags.Output;
                yield return second;
            }
        }
    }
}
