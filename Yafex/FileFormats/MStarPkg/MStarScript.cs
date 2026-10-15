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
            => Parse(mbootScript, long.MaxValue);

        /// <summary>Named <c># File Partition:</c> sections first; images
        /// whose script has none (bare <c>filepartload</c> sequences, e.g.
        /// hisense/toshiba) fall back to offset-named entries.
        /// Out-of-range entries are skipped.</summary>
        public static IEnumerable<MStarPartition> Parse(string mbootScript, long dataLength)
        {
            bool any = false;
            foreach (var part in ParseNamed(mbootScript, dataLength))
            {
                any = true;
                yield return part;
            }
            if (any) yield break;
            foreach (var part in ParseRaw(mbootScript, dataLength))
            {
                yield return part;
            }
            yield break;
        }

        private static IEnumerable<MStarPartition> ParseNamed(string mbootScript, long dataLength)
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

                var parsed = ParseLine(partLoad, partName, dataLength);
                if (parsed != null) yield return parsed;
            }
            yield break;
        }

        private static IEnumerable<MStarPartition> ParseRaw(string mbootScript, long dataLength)
        {
            var seen = new HashSet<(uint, uint)>();
            foreach (var line in Regex.Split(mbootScript, "\r?\n"))
            {
                if (!line.StartsWith("filepartload ")) continue;
                // No section name available: name by file offset.
                var parsed = ParseLine(line, null, dataLength);
                if (parsed == null) continue;
                if (!seen.Add((parsed.Offset, parsed.Size))) continue;
                yield return parsed;
            }
            yield break;
        }

        private static MStarPartition? ParseLine(string partLoad, string? partName, long dataLength)
        {
            var args = partLoad.Split(' ');
            if (args.Length < 5) return null;

            var partOffset = args[3];
            if (partOffset == null) return null;

            var partSize = args[4];
            if (partSize == null) return null;

            MStarPartition? parsed = null;
            try
            {
                // Offsets come with or without a 0x prefix (toshiba uses
                // bare hex); strip it so Convert always sees plain hex.
                var offset = Convert.ToUInt32(StripHexPrefix(partOffset), 16);
                var size = Convert.ToUInt32(StripHexPrefix(partSize), 16);
                if (size == 0 || (long)offset + size > dataLength) return null;
                parsed = new MStarPartition()
                {
                    Name = partName ?? $"img_{offset:X}",
                    Offset = offset,
                    Size = size,
                };
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException)
            {
                return null;
            }
            return parsed;
        }

        private static string StripHexPrefix(string s)
            => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
    }
}
