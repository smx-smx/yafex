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

using Yafex.Fuse;

namespace Yafex.FileFormats.Partinfo
{
    public class PartinfoAddon : IFormatAddon<PartinfoDetectionResult>
    {
        public FileFormat FileFormat => FileFormat.Partinfo;

        public IFormatDetector CreateDetector(IDictionary<string, string> args)
        {
            return new PartinfoDetector();
        }

        public IFormatExtractor CreateExtractor(PartinfoDetectionResult result)
        {
            return new PartinfoExtractor(result);
        }

        public YafexVfs? CreateVfs(IDataSource source, DetectionResult result)
        {
            if (source is not Support.MFile input) return null;
            if (result is not PartinfoDetectionResult typed || typed.Context == null) return null;
            try
            {
                var table = PartinfoTable.Parse(input.Data.Span, typed.Context.PartinfoType);
                VfsMount.TryRoot(input, "partinfo", out var vfs, out var root);
                var keepAlive = new System.Collections.Generic.List<object>();
                foreach (var part in table.partitions)
                {
                    VfsMount.AddRange(root, input, $"{part.name}.bin",
                        (long)part.offset, (long)part.size,
                        $"partition: {part.name}\nfile: {part.filename}\nversion: {part.sw_ver}",
                        keepAlive);
                }
                return vfs;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
