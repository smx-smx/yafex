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
using System.Linq;
using System.Text.RegularExpressions;

namespace Yafex.FileFormats.MStarPkg
{
    public class MStarPartition
    {
        public string Name = "";
        public uint Offset;
        public uint Size;

        public override string ToString()
        {
            return $"{Name} @0x{Offset:X} (0x{Size:X})";
        }
    }

    /// <summary>Shared mboot-script partition walk (extractor + VFS).</summary>
    public static class MStarScript
    {
        public static IEnumerable<MStarPartition> Parse(string mbootScript)
        {
            string search = "# File Partition: ";
            var parts = mbootScript.Split(search);
            if (parts.Length < 2)
            {
                yield break;
            }

            for (int i = 1; i < parts.Length; i++)
            {
                var lines = Regex.Split(parts[i], "\r?\n")
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();

                if (lines.Count < 1) continue;
                var partName = lines[0];

                var partLoad = lines.Where(x => x.StartsWith("filepartload ")).FirstOrDefault();
                if (partLoad == null) continue;

                var args = partLoad.Split(' ');
                if (args.Length < 5) continue;

                var partOffset = args[3];
                if (partOffset == null) continue;

                var partSize = args[4];
                if (partSize == null) continue;

                MStarPartition? parsed = null;
                try
                {
                    parsed = new MStarPartition()
                    {
                        Name = partName,
                        Offset = Convert.ToUInt32(partOffset, 16),
                        Size = Convert.ToUInt32(partSize, 16)
                    };
                }
                catch (Exception ex) when (ex is FormatException || ex is OverflowException)
                {
                    continue;
                }
                yield return parsed;
            }
            yield break;
        }
    }
}
