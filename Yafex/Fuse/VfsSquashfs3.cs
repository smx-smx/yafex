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

using log4net;

using Yafex.FileSystems.Squashfs3;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for squashfs 3.x images (LG EPK2 paks). Reads go through
/// the virtual file via the DiscUtils driver, so nested filesystems never
/// require extraction. Registered ahead of the 4.x expander: the magic is
/// shared, the major version decides.
/// </summary>
public sealed class Squashfs3Expander : IContentExpander
{
    private static readonly ILog log = LogManager.GetLogger(typeof(Squashfs3Expander));

    public string Name => "squashfs3";

    public bool Matches(IVirtualFile file)
    {
        // fork core covers both endiannesses (mount previously missed BE)
        return ExpanderSniff.Head(file, 32, head => Squashfs3FileSystem.HasMagic(head, out _));
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            if (!TryOpen(file, keepAlive, out var reader))
            {
                throw new InvalidDataException("squashfs3 open failed");
            }
            var tmp = new YafexDirectory(file.Name, Helpers.OctalLiteral(0755));
            DiscTree.BuildNodes(tmp, reader);
            return tmp.Tree.Values.Select(l => l.Node);
        });
    }

    public static bool TryOpen(
        IVirtualFile image, ICollection<object> keepAlive, out Squashfs3FileSystem reader)
    {
        reader = null!;
        try
        {
            var stream = image.OpenRead();
            var rdr = new Squashfs3FileSystem(stream);
            keepAlive.Add(rdr);
            reader = rdr;
            return true;
        }
        catch (Exception ex)
        {
            log.Debug($"squashfs3 open of '{image.Name}' failed", ex);
            return false;
        }
    }
}
