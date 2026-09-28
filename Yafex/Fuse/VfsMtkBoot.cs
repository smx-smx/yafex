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
using System.IO;
using System.Linq;

using Yafex.FileFormats.MtkBoot;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for Mediatek boot images: first-stage slice plus the LZHS
/// segments (sliced raw, chained into the LZHS expander on access).
/// </summary>
public sealed class MtkBootExpander : IContentExpander
{
    public string Name => "mtkboot";

    public bool Matches(IVirtualFile file)
    {
        return file.Length >= MtkBoot.PblSize
            && ExpanderSniff.Head(file, MtkBoot.MagicOffset + 32, MtkBoot.HasMagic);
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            var nodes = new List<IVfsNode>();
            long len = file.OpenRead().Length;
            int firstSize = (int)Math.Min(len, MtkBoot.PblSize);
            var first = new SliceVirtualFile("mtk_1bl.bin", file, 0, firstSize);
            keepAlive.Add(first);
            nodes.Add(new PakVfsFile(first, Helpers.OctalLiteral(0444)));
            // known segment slots, by reference layout order
            foreach (var (segOff, name) in new[]
            {
                (MtkBoot.LoaderOff, "mtkloader"), (MtkBoot.LoaderOff1, "mtkloader"),
                (MtkBoot.UbootOff, "uboot"), (MtkBoot.HisenseUbootOff, "uboot"),
            })
            {
                if (segOff + 16 > len) continue;
                var seg = new SliceVirtualFile(name + ".lzhs", file, segOff, len - segOff);
                keepAlive.Add(seg);
                try
                {
                    if (ContentExpanders.TryExpand(seg, keepAlive, out var inner) && inner != null)
                    {
                        inner.EnsureLoaded();
                        foreach (var entry in inner.Tree.Values.Select(l => l.Node))
                        {
                            nodes.Add(entry);
                        }
                    }
                }
                catch (Exception)
                {
                    // not a working LZHS segment here: skip the slot
                }
            }
            return nodes;
        });
    }
}
