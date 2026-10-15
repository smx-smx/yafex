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

using Yafex.FileFormats.Idb;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for IDB image databases: exposes each blob as N.png
/// (decoded PNG when possible) or N.bin (raw fallback), mirroring the
/// extraction naming contract.
/// </summary>
public sealed class IdbExpander : IContentExpander
{
    public string Name => "idb";

    public bool Matches(IVirtualFile file)
    {
        return ExpanderSniff.Head(file, 32, Idb.HasMagic);
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            var nodes = new List<IVfsNode>();
            var buf = file.ReadAllBytes();
            if (!Idb.TryParse(buf, out var table))
            {
                throw new InvalidDataException("not an IDB file");
            }
            int n2 = 0;
            foreach (var e in table.Entries)
            {
                if (e.Size == 0)
                {
                    continue;
                }
                n2++;
                var blob = new byte[e.Size];
                Buffer.BlockCopy(buf, (int)e.Offset, blob, 0, (int)e.Size);
                // same naming contract as extraction: real PNGs keep .png,
                // anything else is exposed raw under .bin
                string name = n2 + ".bin";
                IVirtualFile node = new MemoryVirtualFile(name, blob);
                if (IdbExtractor.IsPng(blob))
                {
                    name = n2 + ".png";
                    node = new MemoryVirtualFile(name, blob);
                }
                else
                {
                    var png = IdbExtractor.TryDecodeToPng(blob);
                    if (png != null)
                    {
                        name = n2 + ".png";
                        node = new MemoryVirtualFile(name, png);
                    }
                }
                keepAlive.Add(node);
                nodes.Add(new PakVfsFile(node, Helpers.OctalLiteral(0444)));
            }
            return nodes;
        });
    }
}
