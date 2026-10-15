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

using Yafex.FileFormats.Ubi;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for UBI images: exposes one file per volume (payloads
/// materialized from their LEBs), chaining each into the nested-format
/// expander on access.
/// </summary>
public sealed class UbiExpander : IContentExpander
{
    public string Name => "ubi";

    public bool Matches(IVirtualFile file)
    {
        try
        {
            if (file.Length < 2048)
            {
                return false;
            }
            var buf = file.ReadHead(4L * 1024 * 1024);
            return UbiFormat.FindStart(buf) >= 0;
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
            byte[] image = file.ReadAvailable();
            long start = UbiFormat.FindStart(image);
            if (start < 0)
            {
                throw new InvalidOperationException("no UBI image found");
            }
            var ubi = UbiFormat.OpenAt(image, start);
            if (ubi == null)
            {
                throw new InvalidOperationException("UBI open failed");
            }
            var nodes = new List<IVfsNode>();
            foreach (var v in ubi.Volumes)
            {
                if (v.VolId >= 0x7F00)
                {
                    continue;
                }
                var mem = new MemoryVirtualFile(UbiFormat.SafeName(v.Name), v.Payload());
                VfsBuilder.AddWithNested(nodes, mem, keepAlive);
            }
            if (nodes.Count == 0)
            {
                throw new InvalidOperationException("no UBI volumes found");
            }
            return nodes;
        });
    }
}
