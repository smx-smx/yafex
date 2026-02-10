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
            switch (ctx.PartinfoType)
            {
                case PartinfoType.MtdInfo:
                    var mtd_pi = data.ReadStruct<MtdInfo.PartmapInfo>();
                    return PartinfoTable.FromMtdInfo(mtd_pi);
                case PartinfoType.PartinfoV1:
                    var p1_pi = data.ReadStruct<PartinfoV1.PartmapInfo>();
                    return PartinfoTable.FromPartinfoV1(p1_pi);
                case PartinfoType.PartinfoV2:
                    var p2_pi = data.ReadStruct<PartinfoV2.PartmapInfo>();
                    return PartinfoTable.FromPartinfoV2(p2_pi);
                default:
                    throw new NotSupportedException("Unsupported partinfo type");
            }
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
                    sb.Append(string.Format(PART_FMT[2],
                        part.filename,
                        part.filesize,
                        part.sw_ver,
                        part.used ? 'U' : 'u',
                        part.valid ? 'V' : 'v',
                        (int)((double)part.filesize / part.size * 100)
                    ));
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public IEnumerable<IDataSource> Extract(IDataSource source)
        {
            string sOut = DumpPartinfo(source.Data.Span);

            var dirName = Path.GetDirectoryName(source.Directory);
            if(dirName == null)
            {
                throw new InvalidOperationException();
            }

            string artifactName = Path.Combine(
                dirName,
                Path.GetFileNameWithoutExtension(source.Directory) + ".txt"
            );

            var artifact = new MemoryDataSource(Encoding.ASCII.GetBytes(sOut))
            {
                Name = artifactName
            };
            yield return artifact;
        }
    }
}
