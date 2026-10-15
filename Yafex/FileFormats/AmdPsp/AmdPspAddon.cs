#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
using System;
using System.Collections.Generic;

using Yafex.Fuse;
using Yafex.Support;

using UefiParser = Yafex.FileFormats.Uefi.Uefi;

namespace Yafex.FileFormats.AmdPsp;

public class AmdPspAddon : FormatAddon<AmdPspDetectionResult>
{
    public override FileFormat FileFormat => FileFormat.AmdPsp;

    public override IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new AmdPspDetector();
    }

    protected override IFormatExtractor CreateExtractor(AmdPspDetectionResult typed)
    {
        return new AmdPspExtractor(typed);
    }

    protected override IFormatLister? CreateLister(AmdPspDetectionResult typed)
    {
        return new AmdPspLister(typed);
    }

    // Lazy mount: carved entries plus UEFI volume slices as range views
    // (no copies), mirroring the extractor including the FV supersede rule.
    public override YafexVfs? CreateVfs(IDataSource input, DetectionResult result)
    {
        if (result is not AmdPspDetectionResult typed) return null;
        try
        {
            VfsMount.TryRoot(input, "amdpsp", out var vfs, out var root);
            var keepAlive = new List<object>();
            var bytes = input.Data.ToArray();
            var volumes = UefiParser.FindVolumes(bytes);
            for (int di = 0; di < typed.Directories.Count; di++)
            {
                var dir = typed.Directories[di];
                string prefix = dir.Kind == AmdPsp.DirKind.Psp ? "psp" : "bios";
                for (int i = 0; i < dir.Entries.Count; i++)
                {
                    var e = dir.Entries[i];
                    if (e.Avail <= 0)
                    {
                        continue;
                    }
                    bool covered = false;
                    foreach (var v in volumes)
                    {
                        if (AmdPspExtractor.Overlaps(e.ResolvedOffset, e.Avail, v.Offset, v.Length))
                        {
                            covered = true;
                            break;
                        }
                    }
                    if (covered)
                    {
                        continue;
                    }
                    VfsMount.AddRange(root, input, $"{prefix}{di}_{e.TypeName}_{i:00}.bin",
                        e.ResolvedOffset, e.Avail,
                        $"amdpsp {dir.CookieName} {e.TypeName}",
                        keepAlive);
                }
            }
            for (int i = 0; i < volumes.Count; i++)
            {
                var v = volumes[i];
                VfsMount.AddRange(root, input, $"fv_{i:00}.bin",
                    v.Offset, v.Length, "uefi firmware volume", keepAlive);
            }
            return vfs;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
