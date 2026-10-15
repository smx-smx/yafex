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

using Yafex.FileFormats.MstarSb;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for MStar secure-boot images: header-boundary slices.
/// </summary>
public sealed class MstarSbExpander : IContentExpander
{
    public string Name => "mstarsb";

    public bool Matches(IVirtualFile file)
    {
        return ExpanderSniff.Head(file, 64, MstarSb.HasMagic);
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            var nodes = new List<IVfsNode>();
            var buf = file.ReadAllBytes();
            // same split-at-boundaries as extraction
            var headers = MstarSb.FindHeaders(buf);
            for (int i = 0; i < headers.Count; i++)
            {
                long start = headers[i].Offset;
                long end = i + 1 < headers.Count ? headers[i + 1].Offset : buf.Length;
                if (end <= start)
                {
                    continue;
                }
                var seg = new SliceVirtualFile($"sec_{start:X}.bin", file, start, end - start);
                keepAlive.Add(seg);
                nodes.Add(new PakVfsFile(seg, Helpers.OctalLiteral(0444)));
            }
            return nodes;
        });
    }
}
