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

using Yafex.FileSystems.Jffs2;

using log4net;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for JFFS2 images (e.g. EPK1 mode.pak).
/// </summary>
public sealed class Jffs2Expander : IContentExpander
{
    private static readonly ILog log = LogManager.GetLogger(typeof(Jffs2Expander));

    public string Name => "jffs2";

    public bool Matches(IVirtualFile file)
    {
        // probe up to 64KB: Detect requires the whole first node
        // (totlen <= length), so a 12-byte peek only matches images
        // leading with a cleanmarker and misses real images that open
        // with a dirent/inode (e.g. Askey boot regions)
        try
        {
            using var s = file.OpenRead();
            long len = Math.Min(s.Length, 65536);
            if (len < 12) return false;
            var buf = new byte[len];
            int off = 0;
            while (off < buf.Length)
            {
                int n = s.Read(buf, off, buf.Length - off);
                if (n <= 0) return false;
                off += n;
            }
            using var ms = new MemoryStream(buf, writable: false);
            return Jffs2FileSystem.Detect(ms);
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
            if (!TryOpen(file, keepAlive, out var reader))
            {
                throw new InvalidDataException("jffs2 open failed");
            }
            var tmp = new YafexDirectory(file.Name, Helpers.OctalLiteral(0755));
            DiscTree.BuildNodes(tmp, reader);
            return tmp.Tree.Values.Select(l => l.Node);
        });
    }

    public static bool TryOpen(
        IVirtualFile image, ICollection<object> keepAlive, out Jffs2FileSystem reader)
    {
        reader = null!;
        try
        {
            var stream = image.OpenRead();
            var rdr = new Jffs2FileSystem(stream);
            keepAlive.Add(rdr);
            reader = rdr;
            return true;
        }
        catch (Exception ex)
        {
            log.Debug($"jffs2 open of '{image.Name}' failed", ex);
            return false;
        }
    }
}
