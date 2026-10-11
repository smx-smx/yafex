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

using DiscUtils.Ufs;

using log4net;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for UFS (Berkeley FFS) images, UFS1/UFS2 either endianness
/// (e.g. Panasonic SDDL/FreeBSD firmware payloads).
/// </summary>
public sealed class UfsExpander : IContentExpander
{
    private static readonly ILog log = LogManager.GetLogger(typeof(UfsExpander));

    public string Name => "ufs";

    public bool Matches(IVirtualFile file)
    {
        // superblock magic at +1372 (floppy), +9564 (UFS1), +66908 (UFS2)
        // or +263516 (piggy); probes are sized to cover each while still
        // matching small images. Field validation only (no inode walks);
        // full validation runs in TryOpen on expand.
        return ExpanderSniff.Head(file, 263520, head => UfsFileSystem.HeadMatches(head))
            || ExpanderSniff.Head(file, 66912, head => UfsFileSystem.HeadMatches(head))
            || ExpanderSniff.Head(file, 9568, head => UfsFileSystem.HeadMatches(head));
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            if (!TryOpen(file, keepAlive, out var reader))
            {
                throw new InvalidDataException("ufs open failed");
            }
            var tmp = new YafexDirectory(file.Name, Helpers.OctalLiteral(0755));
            DiscTree.BuildNodes(tmp, reader);
            return tmp.Tree.Values.Select(l => l.Node);
        });
    }

    public static bool TryOpen(
        IVirtualFile image, ICollection<object> keepAlive, out UfsFileSystem reader)
    {
        reader = null!;
        try
        {
            var stream = image.OpenRead();
            var rdr = new UfsFileSystem(stream);
            keepAlive.Add(rdr);
            reader = rdr;
            return true;
        }
        catch (Exception ex)
        {
            log.Debug($"ufs open of '{image.Name}' failed", ex);
            return false;
        }
    }
}
