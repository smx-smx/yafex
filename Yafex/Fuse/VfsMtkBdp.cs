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

using Yafex.FileFormats.MtkBdp;
using Yafex.FileSystems.MtkBdp;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for MediaTek Blu-ray update images: exposes one file
/// per assembled partition, chaining each into the nested-format
/// expander on access.
/// </summary>
public sealed class MtkBdpExpander : IContentExpander
{
    public string Name => "mtkbdp";

    public bool Matches(IVirtualFile file)
    {
        // hint probe only: the PITIT copies sit at the tail (and a stale
        // one up front), while validation needs the whole file. Expand
        // runs the full chained validation and fails loudly otherwise.
        try
        {
            if (file.Length < 128)
            {
                return false;
            }
            int window = (int)Math.Min(file.Length, 65536);
            return ExpanderSniff.Head(file, window, head => MtkBdpParser.FindBytes(head, MtkBdpConstants.PititMagic) >= 0)
                || ExpanderSniff.At(file, Math.Max(0, file.Length - window), window,
                    tail => MtkBdpParser.FindBytes(tail, MtkBdpConstants.PititMagic) >= 0);
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
            var parts = MtkBdpParser.Open(image);
            if (parts == null)
            {
                throw new InvalidOperationException("no MediaTek partitions found");
            }
            var nodes = new List<IVfsNode>();
            foreach (var p in parts.OrderBy(x => x.Part.Id))
            {
                var mem = new MemoryVirtualFile(
                    MtkBdpExtractor.SafeName(p.Part.Name) + ".bin", p.Assemble(image));
                VfsBuilder.AddWithNested(nodes, mem, keepAlive);
            }
            if (nodes.Count == 0)
            {
                throw new InvalidOperationException("no MediaTek partitions found");
            }
            return nodes;
        });
    }
}
