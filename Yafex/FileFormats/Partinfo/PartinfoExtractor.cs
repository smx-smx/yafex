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
using Yafex.Support;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Smx.SharpIO.Memory.Buffers;

namespace Yafex.FileFormats.Partinfo
{
    public class PartinfoExtractor : IFormatExtractor
    {
        private PartinfoContext ctx;

        public PartinfoExtractor(PartinfoDetectionResult result)
        {
            this.ctx = result.Context;
        }

        private PartinfoTable GetPartinfoTable(ReadOnlySpan64<byte> data)
        {
            return PartinfoTable.Parse(data, ctx.PartinfoType);
        }

        private static readonly string[] HEADER_FMT = new string[]{
            "Partition Information ({0}) ---------------------------------------------------------------------------------",
            "cur epk ver : 0x{0:X6}",
            "old epk ver : 0x{0:X6}"
        };

        private static readonly string[] PART_FMT = new string[] {
            "[{0,2}] \"{1,-12}\" : 0x{2:X9}-0x{3:X9} (0x{4:X9})",
            " {0}{1}{2}{3}{4}", // flags
			" : \"{0,-20}\"[{1}] - 0x{2:X6} : ({3}/{4}) [{5,3}%]",
            "[{0,2}] Empty\n"
        };

        private string DumpPartinfo(ReadOnlySpan64<byte> data)
        {
            PartinfoTable tbl = GetPartinfoTable(data);

            StringBuilder sb = new StringBuilder();

            sb.AppendLine(string.Format(HEADER_FMT[0],
                Enum.GetName(typeof(PartinfoType), ctx.PartinfoType)));
            sb.AppendLine();

            sb.AppendLine(string.Format(HEADER_FMT[1], tbl.cur_epk_ver));
            sb.AppendLine(string.Format(HEADER_FMT[2], tbl.old_epk_ver));
            sb.AppendLine();

            for (int i = 0; i < tbl.npartition; i++)
            {
                var part = tbl.partitions[i];
                sb.Append(string.Format(PART_FMT[0],
                    i, part.name,
                    part.offset,
                    part.offset + part.size,
                    part.size));


                char cacheOrData = '-';
                if (part.mask_flags.HasFlag(PartinfoPartFlags.Cache))
                {
                    cacheOrData = 'C';
                }
                else if (part.mask_flags.HasFlag(PartinfoPartFlags.Data))
                {
                    cacheOrData = 'D';
                }

                sb.Append(string.Format(PART_FMT[1],
                    part.mask_flags.HasFlag(PartinfoPartFlags.Fixed) ? 'F' : '-',
                    part.mask_flags.HasFlag(PartinfoPartFlags.Master) ? 'M' : '-',
                    part.mask_flags.HasFlag(PartinfoPartFlags.Secured) ? 'S' : '-',
                    part.mask_flags.HasFlag(PartinfoPartFlags.IdKey) ? 'I' : '-',
                    cacheOrData
                ));

                if (part.mask_flags.HasFlag(PartinfoPartFlags.Erase))
                {
                    sb.Append('*');
                }

                if (part.filename.Length > 0)
                {
                    long pct = part.size > 0 ? (int)((double)part.filesize / part.size * 100) : 0;
                    sb.Append(string.Format(PART_FMT[2],
                        part.filename,
                        part.filesize,
                        part.sw_ver,
                        part.used ? 'U' : 'u',
                        part.valid ? 'V' : 'v',
                        pct
                    ));
                }
                if (part.extra != 0)
                {
                    sb.Append(string.Format(" extra=0x{0:X}", part.extra));
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public IEnumerable<IDataSource> Extract(IDataSource source)
        {
            string sOut = DumpPartinfo(source.Data.Span);

            // Top-level artifacts carry a bare filename (no directory): don't crash on those.
            string baseName = Path.GetFileNameWithoutExtension(source.Name ?? source.Directory ?? "partinfo");
            if (baseName.Length == 0)
            {
                baseName = "partinfo";
            }
            string artifactName = baseName + ".txt";

            var artifact = new MemoryDataSource(Encoding.ASCII.GetBytes(sOut))
            {
                Name = artifactName
            };
            artifact.AddMetadata(new Metadata.OutputFileName(artifactName));
            artifact.SetChildOf(source);
            // pin the inherited dir as an explicit output dir so the
            // framework creates it (standalone -d may not exist yet)
            var outDir = artifact.GetMetadata<Metadata.BaseDirectoryPath>().LastOrDefault()?.DirectoryPath;
            if (outDir != null)
            {
                artifact.AddMetadata(new Metadata.OutputDirectoryName(outDir));
            }
            yield return artifact;
        }
    }
}
