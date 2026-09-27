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

namespace Yafex.FileFormats.MStarPkg
{
    public class MStarPkgExtractor : IFormatExtractor
    {
        public MStarPkgExtractor()
        {
        }

        private static IEnumerable<MStarPartition> ParseMBootScript(string mbootScript)
            => MStarScript.Parse(mbootScript);

        public IEnumerable<IDataSource> Extract(IDataSource source)
        {
            var mbootScript = Encoding.ASCII.GetString(
                source.Data.Slice(0, MStarPkgDetector.MBOOT_SCRIPT_SIZE).ToArray()
            );
            var partitions = ParseMBootScript(mbootScript);

            var baseDir = Path.Combine(source.RequireBaseDirectory(), Path.GetFileNameWithoutExtension(source.Directory));
            Directory.CreateDirectory(baseDir);

            foreach (var part in partitions)
            {
                var destPath = Path.Combine(baseDir, $"{part.Name}.pak");

                var artifact = new MemoryDataSource(
                    source
                        .Data.Span
                        .Slice(part.Offset, part.Size)
                        .ToArray()
                )
                {
                    Name = $"{part.Name}.pak"
                };
                yield return artifact;
            }
        }
    }
}
