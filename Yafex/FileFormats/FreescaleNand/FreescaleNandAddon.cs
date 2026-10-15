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
using Yafex.Support;

namespace Yafex.FileFormats.FreescaleNand
{
    public class FreescaleNandAddon : IFormatAddon
    {
        public FileFormat FileFormat => FileFormat.FreescaleNand;

        public IFormatDetector CreateDetector(IDictionary<string, string> args)
        {
            return new FreescaleNandDetector();
        }

        public IFormatExtractor CreateExtractor(DetectionResult result)
        {
            return new FreescaleNandExtractor();
        }

        public YafexVfs? CreateVfs(IDataSource source, DetectionResult result)
        {
            if (source is not Support.MFile input) return null;
            try
            {
                var fcb = input.Data.ReadStruct<mx28_nand_fcb>(0xC);
                VfsMount.TryRoot(input, "freescale", out var vfs, out var root);
                var keepAlive = new System.Collections.Generic.List<object>();
                AddFirmware(root, input, "firmware1.bin", fcb.firmware1_starting_sector,
                    fcb.sectors_in_firmware1, fcb, keepAlive);
                AddFirmware(root, input, "firmware2.bin", fcb.firmware2_starting_sector,
                    fcb.sectors_in_firmware2, fcb, keepAlive);
                return vfs;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void AddFirmware(YafexDirectory root, Support.MFile input, string name,
            uint startSector, uint sectors, mx28_nand_fcb fcb, System.Collections.Generic.List<object> keepAlive)
        {
            if (fcb.page_data_size == 0 || fcb.total_page_size < fcb.page_data_size) return;
            if (sectors == 0 || sectors > 10000000) return;
            var ranges = new System.Collections.Generic.List<(long, long)>();
            for (long i = 0; i < sectors; i++)
            {
                long off = (long)startSector * fcb.page_data_size + i * fcb.total_page_size;
                if (off < 0 || off >= input.Data.Length) break;
                ranges.Add((off, System.Math.Min((long)fcb.page_data_size, input.Data.Length - off)));
            }
            if (ranges.Count == 0) return;
            var image = new MultiRangeVirtualFile(name, input, ranges);
            VfsBuilder.AddImage(root, image,
                VfsImageViews.Decrypted | VfsImageViews.Metadata | VfsImageViews.Expanded,
                keepAlive,
                describe: () => $"sectors: {sectors}\npage data: {fcb.page_data_size}\npage total: {fcb.total_page_size}");
        }
    }
}
