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

using Yafex.FileFormats.Spv;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for SG2 flash images: exposes one file per populated
/// bin, chaining each into the nested-format expander on access.
/// </summary>
public sealed class SpvExpander : IContentExpander
{
    public string Name => "spv";

    public bool Matches(IVirtualFile file)
    {
        return ExpanderSniff.Head(file, SpvParser.HeaderSize, head =>
        {
            if (SpvParser.ReadU32LE(head, 4) != SpvParser.TagMagic) return false;
            uint total = SpvParser.ReadU32LE(head, 28);
            return total >= 1 && total <= SpvParser.MaxBins;
        });
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            byte[] image = file.ReadAvailable();
            if (!SpvParser.TryParseHeader(image, out var bins))
            {
                throw new InvalidOperationException("no SPV bins found");
            }
            var nodes = new List<IVfsNode>();
            foreach (var b in bins)
            {
                long length = Math.Min((long)b.Size, image.Length - b.Offset);
                if (length <= 0) continue;
                // offsets here are relative to the region start, which the
                // parser computed against the same base image
                var slice = new SliceVirtualFile(
                    SpvParser.SafeName(b.Name) + ".bin", file, b.Offset, length);
                keepAlive.Add(slice);
                nodes.Add(new PakVfsFile(slice, Helpers.OctalLiteral(0444)));
                try
                {
                    if (ContentExpanders.TryExpand(slice, keepAlive, out var nested) && nested != null)
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
                    // keep just the slice
                }
            }
            if (nodes.Count == 0)
            {
                throw new InvalidOperationException("no SPV bins found");
            }
            return nodes;
        });
    }
}
