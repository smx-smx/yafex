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

using Yafex.Fuse;

namespace Yafex.FileFormats.Uefi;

public class UefiAddon : FormatAddon<UefiDetectionResult>
{
    public override FileFormat FileFormat => FileFormat.Uefi;

    public override IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new UefiDetector();
    }

    protected override IFormatExtractor CreateExtractor(UefiDetectionResult typed)
    {
        return new UefiExtractor(typed);
    }

    protected override IFormatLister? CreateLister(UefiDetectionResult typed)
    {
        return new UefiLister(typed);
    }

    public override YafexVfs? CreateVfs(IDataSource input, DetectionResult result)
    {
        try
        {
            VfsMount.TryRoot(input, "uefi", out var vfs, out var root);
            var keepAlive = new List<object>();
            var image = new RangeVirtualFile(input.Name ?? "image.rom", input, 0, input.Data.Length);
            VfsBuilder.AddImage(root, image, VfsImageViews.All, keepAlive,
                raw: image,
                describe: () => Describe(input));
            return vfs;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Describe(IDataSource input)
    {
        var bytes = input.Data.ToArray();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"UEFI firmware: {input.Name} ({bytes.Length} bytes)");
        foreach (var r in Uefi.ReadRegions(bytes))
        {
            sb.AppendLine($"region {r.Name} @0x{r.Offset:X} len=0x{r.Length:X}");
        }
        foreach (var v in Uefi.FindVolumes(bytes))
        {
            sb.AppendLine($"FV @0x{v.Offset:X} len=0x{v.Length:X} files={Uefi.ReadFiles(bytes, v).Count}");
        }
        foreach (var e in Uefi.ReadFit(bytes).Where(e => e.Type != 0x00 && e.FileOffset >= 0))
        {
            sb.AppendLine($"FIT type={UefiStructures.FitTypeName(e.Type)} @0x{e.FileOffset:X}");
        }
        return sb.ToString();
    }
}
