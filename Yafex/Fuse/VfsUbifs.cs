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

using Yafex.FileSystems.Ubifs;

using log4net;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for UBIFS volume payloads (e.g. UBI data volumes).
/// </summary>
public sealed class UbifsExpander : IContentExpander
{
    private static readonly ILog log = LogManager.GetLogger(typeof(UbifsExpander));

    public string Name => "ubifs";

    public bool Matches(IVirtualFile file)
    {
        // head must cover the whole opening superblock node (4K live)
        return ExpanderSniff.Head(file, 8192, head =>
        {
            using var ms = new MemoryStream(head, writable: false);
            return UbifsFileSystem.Detect(ms);
        });
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            if (!TryOpen(file, keepAlive, out var reader))
            {
                throw new InvalidDataException("ubifs open failed");
            }
            var tmp = new YafexDirectory(file.Name, Helpers.OctalLiteral(0755));
            DiscTree.BuildNodes(tmp, reader);
            return tmp.Tree.Values.Select(l => l.Node);
        });
    }

    public static bool TryOpen(
        IVirtualFile image, ICollection<object> keepAlive, out UbifsFileSystem reader)
    {
        reader = null!;
        try
        {
            var stream = image.OpenRead();
            var rdr = new UbifsFileSystem(stream);
            keepAlive.Add(rdr);
            reader = rdr;
            return true;
        }
        catch (Exception ex)
        {
            log.Debug($"ubifs open of '{image.Name}' failed", ex);
            return false;
        }
    }
}
