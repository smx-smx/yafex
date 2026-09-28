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

using Yafex.FileFormats.Nfsb;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for NFSB wrappers (e.g. GP4 base.pak): exposes the payload
/// past the 0x1000-byte header, chaining into the nested-format expander when
/// the payload itself is a filesystem (squashfs).
/// </summary>
public sealed class NfsbExpander : IContentExpander
{
    public string Name => "nfsb";

    public bool Matches(IVirtualFile file)
    {
        return file.Length > Nfsb.HeaderSize
            && ExpanderSniff.Head(file, 64, Nfsb.HasMagic);
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            var inner = new SliceVirtualFile(file.Name + ".unnfsb", file,
                Nfsb.HeaderSize, file.OpenRead().Length - Nfsb.HeaderSize);
            keepAlive.Add(inner);
            var nodes = new List<IVfsNode> { new PakVfsFile(inner, Helpers.OctalLiteral(0444)) };
            // nested filesystem (squashfs): surface its tree next to the blob
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
