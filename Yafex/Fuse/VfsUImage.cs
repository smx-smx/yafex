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

using Yafex.FileFormats.UImage;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for u-boot legacy images (e.g. kern.pak): exposes the payload
/// past the 64-byte header, chaining into the nested-format expander when the
/// payload itself is compressed (often LG-LZO).
/// </summary>
public sealed class UImageExpander : IContentExpander
{
    public string Name => "uimage";

    public bool Matches(IVirtualFile file)
    {
        try
        {
            using var probe = file.OpenRead();
            var header = new byte[UImage.HeaderSize];
            int off = 0;
            while (off < header.Length)
            {
                int n = probe.Read(header, off, header.Length - off);
                if (n <= 0) return false;
                off += n;
            }
            return Yafex.FileFormats.UImage.UImage.HasHeader(header);
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
            var inner = new SliceVirtualFile(file.Name + ".unpaked", file,
                UImage.HeaderSize, file.OpenRead().Length - UImage.HeaderSize);
            keepAlive.Add(inner);
            var nodes = new List<IVfsNode> { new PakVfsFile(inner, Helpers.OctalLiteral(0444)) };
            try
            {
                if (ContentExpanders.TryExpand(inner, keepAlive, out var nested) && nested != null)
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
                // keep just the stripped blob
            }
            return nodes;
        });
    }
}
