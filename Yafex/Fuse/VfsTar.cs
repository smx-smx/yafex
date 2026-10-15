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

using Yafex.FileFormats.Tar;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for plain ustar archives (including ones nested inside
/// firmware containers): exposes one file per member, chaining each into
/// the nested-format expander on access.
/// </summary>
public sealed class TarExpander : IContentExpander
{
    public string Name => "tar";

    public bool Matches(IVirtualFile file)
    {
        try
        {
            if (file.Length < 1024)
            {
                return false;
            }
            // the head window may end mid-archive: check the header only,
            // Expand walks (and validates) the whole archive
            var buf = file.ReadHead(128L * 1024);
            return TarFormat.FindHeader(buf) >= 0;
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
            long start = TarFormat.FindStart(image);
            if (start < 0)
            {
                throw new InvalidOperationException("no tar archive found");
            }
            var root = new YafexDirectory("", Helpers.OctalLiteral(0755));
            using var stream = new MemoryStream(image, (int)start, image.Length - (int)start, false);
            var tar = new YafexTarReader(stream);
            foreach (var e in tar.Entries)
            {
                var parts = VfsBuilder.SplitMemberPath(e.Name);
                if (parts == null) continue;
                if (e.IsDirectory)
                {
                    VfsBuilder.EnsureDirectory(root, parts);
                    continue;
                }
                var dir = VfsBuilder.EnsureDirectory(root, parts[..^1]);
                var nodes = new List<IVfsNode>();
                var slice = new SliceVirtualFile(parts[^1], file, start + e.Offset, e.Length);
                VfsBuilder.AddWithNested(nodes, slice, keepAlive);
                nodes.ForEach(dir.AddNode);
            }
            if (root.Tree.Count == 0)
            {
                throw new InvalidOperationException("no tar members found");
            }
            return root.Tree.Values.Select(l => l.Node).ToList();
        });
    }
}
