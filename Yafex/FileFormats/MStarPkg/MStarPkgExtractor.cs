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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using log4net;

namespace Yafex.FileFormats.MStarPkg
{
    public class MStarPkgExtractor : IFormatExtractor
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(MStarPkgExtractor));

        public MStarPkgExtractor()
        {
        }

        private static IEnumerable<MStarPartition> ParseMBootScript(string mbootScript, long dataLength)
            => MStarScript.Parse(mbootScript, dataLength);

        public IEnumerable<IDataSource> Extract(IDataSource source)
        {
            var scriptLen = (int)Math.Min(
                MStarPkgDetector.SCRIPT_PARSE_SIZE, source.Data.Length);
            var mbootScript = Encoding.ASCII.GetString(
                source.Data.Slice(0, scriptLen).ToArray()
            );
            var partitions = ParseMBootScript(mbootScript, source.Data.Length);

            var stem = Path.GetFileNameWithoutExtension(
                source.Name ?? "mstar");
            var baseDir = ArtifactBuilder.VersionedDir(source, stem);
            source.AddMetadata(new Metadata.BaseDirectoryPath(baseDir));

            foreach (var part in partitions)
            {
                var fileName = $"{part.Name}.pak";
                var artifact = ArtifactBuilder.FlatIn(source, baseDir, fileName,
                    source.Data.Span.Slice(part.Offset, part.Size).ToArray());
                artifact.Flags |= DataSourceFlags.ProcessFurther;
                yield return artifact;
            }

            // Flash-plan groups (lz4 -d + sparse_write chains): replay each
            // target's segments into one raw image, additive to the .pak
            // segments above. A broken group is skipped loudly, never
            // partially emitted.
            foreach (var group in MStarFlashPlan.ParseSparseWrites(mbootScript, source.Data.Length))
            {
                if (group.Segments.Count == 0) continue;
                var fileName = $"{group.Target}.raw.bin";
                IArtifactBuffer buff = ArtifactBuilder.BeginBuffer(
                    baseDir, fileName, DataSourceFlags.ProcessFurther, SpoolHint.File);
                try
                {
                    MstarSparseCombiner.Combine(source, group, buff);
                }
                catch (Exception ex) when (ex is InvalidDataException
                    || ex is ArgumentException || ex is OverflowException)
                {
                    buff.Dispose();
                    log.Warn($"Skipping flash group '{group.Target}': {ex.Message}");
                    continue;
                }
                yield return ArtifactBuilder.Complete(buff, source, baseDir);
            }
        }
    }
}
