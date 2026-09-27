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

using log4net;

using DiscUtils.Ext;

namespace Yafex.Fuse;

/// <summary>
/// Shared ext4 helpers for any VFS: probe an <see cref="IVirtualFile"/> for an
/// ext2/3/4 image (superblock magic at +0x438, rev >= 0) and expose its tree
/// as VFS nodes. Reads go through the virtual file, so nested filesystems
/// never require extraction.
/// </summary>
public static class VfsExt
{
    public const ushort ExtMagic = 0xEF53;
    public const long SuperblockMagicOffset = 1024 + 0x38;

    public static bool TryOpen(
        IVirtualFile image, ICollection<object> keepAlive, out ExtFileSystem fs)
    {
        fs = null!;
        try
        {
            using var probe = image.OpenRead();
            if (probe.Length < SuperblockMagicOffset + 2) return false;
            probe.Seek(SuperblockMagicOffset, SeekOrigin.Begin);
            var magic = new byte[2];
            int off = 0;
            while (off < magic.Length)
            {
                int n = probe.Read(magic, off, magic.Length - off);
                if (n <= 0) return false;
                off += n;
            }
            if (BitConverter.ToUInt16(magic, 0) != ExtMagic) return false;

            var stream = image.OpenRead();
            var ext = new ExtFileSystem(stream);
            // keep the fs (and its stream) alive for the whole mount session
            keepAlive.Add(ext);
            fs = ext;
            return true;
        }
        catch (Exception ex)
        {
            log.Debug($"ext open of '{image.Name}' failed", ex);
            return false;
        }
    }

    private static readonly ILog log = LogManager.GetLogger(typeof(VfsExt));
}
