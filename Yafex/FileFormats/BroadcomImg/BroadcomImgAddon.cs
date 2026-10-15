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

public class BroadcomImgAddon : IFormatAddon
{
    public FileFormat FileFormat => FileFormat.BroadcomImg;

    public IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new BroadcomImgDetector();
    }

    public IFormatExtractor CreateExtractor(DetectionResult result)
    {
        if (result is not BroadcomImgDetectionResult typed || typed.Image == null)
        {
            throw new ArgumentException(nameof(result));
        }
        return new BroadcomImgExtractor(typed);
    }

    public IFormatLister? CreateLister(DetectionResult result)
    {
        return result is BroadcomImgDetectionResult typed && typed.Image != null
            ? new BroadcomImgLister(typed)
            : null;
    }

    // Lazy mount: one range view per flash region (no copies). Content
    // expansion (JFFS2 tree, UBI volumes, nested UBIFS) is handled by the
    // expander registry on first access.
    public YafexVfs? CreateVfs(IDataSource source, DetectionResult result)
    {
        if (source is not MFile input)
        {
            return null;
        }
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
