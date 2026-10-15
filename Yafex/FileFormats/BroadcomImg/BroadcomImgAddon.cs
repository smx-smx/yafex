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

namespace Yafex.FileFormats.BroadcomImg;

public class BroadcomImgAddon : FormatAddon<BroadcomImgDetectionResult>
{
    public override FileFormat FileFormat => FileFormat.BroadcomImg;

    public override IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new BroadcomImgDetector();
    }

    protected override bool IsUsable(BroadcomImgDetectionResult result) => result.Image != null;

    protected override IFormatExtractor CreateExtractor(BroadcomImgDetectionResult typed)
    {
        return new BroadcomImgExtractor(typed);
    }

    protected override IFormatLister? CreateLister(BroadcomImgDetectionResult typed)
    {
        return new BroadcomImgLister(typed);
    }

    // Lazy mount: one range view per flash region (no copies). Content
    // expansion (JFFS2 tree, UBI volumes, nested UBIFS) is handled by the
    // expander registry on first access.
    public override YafexVfs? CreateVfs(IDataSource input, DetectionResult result)
    {
        if (result is not BroadcomImgDetectionResult typed || typed.Image == null)
        {
            return null;
        }
        try
        {
            VfsMount.TryRoot(input, "broadcomimg", out var vfs, out var root);
            var keepAlive = new List<object>();
            VfsMount.AddRange(root, input, "boot.jffs2", 0, typed.UbiStart,
                "jffs2 boot region", keepAlive);
            VfsMount.AddRange(root, input, "root.ubi", typed.UbiStart,
                typed.Image.Length - typed.UbiStart, "ubi region", keepAlive);
            return vfs;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
