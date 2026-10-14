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

using Yafex.FileFormats.AdbImg;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for D-Link yIMG images: exposes the payload region and
/// the signature blob, chaining the payload into its nested-format
/// expander on access.
/// </summary>
public sealed class AdbImgExpander : IContentExpander
{
    public string Name => "adbimg";

    public bool Matches(IVirtualFile file)
    {
        return ExpanderSniff.Head(file, AdbImgParser.OuterHeaderSize + 16, head =>
        {
            if (AdbImgParser.ReadAsciiZ(head, 0, 6) != "yIMG1") return false;
            return AdbImgParser.ReadAsciiZ(head, AdbImgParser.OuterHeaderSize, 6) == "mIMG1";
        });
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            byte[] image;
            using (var s = file.OpenRead())
            {
                image = new byte[s.Length];
                int off = 0;
                while (off < image.Length)
                {
                    int n = s.Read(image, off, image.Length - off);
                    if (n <= 0) break;
                    off += n;
                }
            }
            if (!AdbImgParser.TrySplit(image, out long start, out long end))
            {
                throw new InvalidOperationException("no yIMG payload found");
            }
            var nodes = new List<IVfsNode>();
            var regions = new List<SliceVirtualFile>
            {
                new SliceVirtualFile("payload.jffs2", file, start, end - start),
            };
            if (end < image.Length)
            {
                regions.Add(new SliceVirtualFile("signature.bin", file, end, image.Length - end));
            }
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
