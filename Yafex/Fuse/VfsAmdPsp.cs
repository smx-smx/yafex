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

using Yafex.FileFormats.AmdPsp;

using UefiParser = Yafex.FileFormats.Uefi.Uefi;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for AMD SPI flash images (EFS + PSP/BIOS directories):
/// exposes carved entries plus UEFI firmware volume slices (which supersede
/// overlapping BIOS entries, mirroring the extractor), chaining each into
/// the nested-format expander on access.
/// </summary>
public sealed class AmdPspExpander : IContentExpander
{
    public string Name => "amdpsp";

    public bool Matches(IVirtualFile file)
    {
        try
        {
            if (file.Length < AmdPsp.DirHeaderSize + AmdPsp.PspEntrySize)
            {
                return false;
            }
            var buf = file.ReadHead(1L << 26);
            return AmdPsp.FindEfs(buf).Count > 0 || AmdPsp.FindDirectories(buf).Count > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            byte[] image = file.ReadAvailable();
            var dirs = AmdPsp.FindDirectories(image);
            if (dirs.Count == 0)
            {
                throw new InvalidOperationException("no AMD PSP directories found");
            }
            var nodes = new List<IVfsNode>();
            void AddSlice(string name, long offset, long size, string? meta)
            {
                var slice = new SliceVirtualFile(name, file, offset, size);
                VfsBuilder.AddWithNested(nodes, slice, keepAlive);
            }
            var volumes = UefiParser.FindVolumes(image);
            for (int di = 0; di < dirs.Count; di++)
            {
                var dir = dirs[di];
                string prefix = dir.Kind == AmdPsp.DirKind.Psp ? "psp" : "bios";
                for (int i = 0; i < dir.Entries.Count; i++)
                {
                    var e = dir.Entries[i];
                    if (e.Avail <= 0)
                    {
                        continue;
                    }
                    bool covered = volumes.Any(v =>
                        AmdPspExtractor.Overlaps(e.ResolvedOffset, e.Avail, v.Offset, v.Length));
                    if (covered)
                    {
                        continue;
                    }
                    AddSlice($"{prefix}{di}_{e.TypeName}_{i:00}.bin",
                        e.ResolvedOffset, e.Avail,
                        $"amdpsp {dir.CookieName} {e.TypeName}");
                }
            }
            for (int i = 0; i < volumes.Count; i++)
            {
                var v = volumes[i];
                AddSlice($"fv_{i:00}.bin", v.Offset, v.Length, "uefi firmware volume");
            }
            return nodes;
        });
    }
}
