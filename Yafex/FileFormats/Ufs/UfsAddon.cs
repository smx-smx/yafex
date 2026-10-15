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

namespace Yafex.FileFormats.Ufs;

public class UfsAddon : FormatAddon<UfsDetectionResult>
{
    public override FileFormat FileFormat => FileFormat.Ufs;

    public override IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new UfsDetector();
    }

    protected override bool IsUsable(UfsDetectionResult result) => result.Image != null;

    protected override IFormatExtractor CreateExtractor(UfsDetectionResult typed)
    {
        return new UfsExtractor(typed);
    }

    // Lazy top-level mount: the expander subtree hangs off the VFS root.
    // Lifetime: the factory closure retains the image, the keep-alive
    // set and the reader for as long as the node is reachable.
    public override YafexVfs? CreateVfs(IDataSource input, DetectionResult result)
    {
        if (result is not UfsDetectionResult typed || typed.Image == null) return null;
        try
        {
            var vfs = new YafexVfs();
            var image = new RangeVirtualFile(input.Name ?? "ufs.bin", input, 0, input.Data.Length);
            var node = new UfsExpander().Expand(image, new List<object>());
            vfs.Root.AddNode(node);
            return vfs;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
