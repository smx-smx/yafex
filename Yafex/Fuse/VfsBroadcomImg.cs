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

using Yafex.FileFormats.BroadcomImg;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for raw flash layouts (JFFS2 boot region plus UBI
/// region): exposes both region views and chains each into its own
/// nested-format expander (UBIFS volumes expand in turn).
/// </summary>
public sealed class BroadcomImgExpander : IContentExpander
{
    public string Name => "broadcomimg";

    public bool Matches(IVirtualFile file)
    {
        // probe window covers the boot region plus the first UBI
        // eraseblocks (live layouts place UBI a few MB in); images with
        // a farther UBI region still detect via the CLI detector
        try
        {
            if (file.Length < BroadcomImgParser.MinUbiStart + 4096)
            {
                return false;
            }
            var buf = file.ReadHead(8L * 1024 * 1024);
            return BroadcomImgParser.TrySplit(buf, out _);
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
            byte[] head = file.ReadHead(8L * 1024 * 1024);
            if (!BroadcomImgParser.TrySplit(head, out long ubiStart))
            {
                throw new InvalidOperationException("no Broadcom flash layout found");
            }
            var nodes = new List<IVfsNode>();
            var regions = new[]
            {
                new SliceVirtualFile("boot.jffs2", file, 0, ubiStart),
                new SliceVirtualFile("root.ubi", file, ubiStart, file.Length - ubiStart),
            };
            foreach (var region in regions)
            {
                keepAlive.Add(region);
                nodes.Add(new PakVfsFile(region, Helpers.OctalLiteral(0444)));
                try
                {
                    if (ContentExpanders.TryExpand(region, keepAlive, out var nested) && nested != null)
                    {
                        nested.EnsureLoaded();
                        foreach (var entry in nested.Tree.Values.Select(l => l.Node))
                        {
                            nodes.Add(entry);
                        }
                    }
                }
                catch (Exception)
                {
                    // keep just the region file
                }
            }
            return nodes;
        });
    }
}
