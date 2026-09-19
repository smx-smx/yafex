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

using DiscUtils;
using DiscUtils.SquashFs;

namespace Yafex.Fuse;

/// <summary>
/// Shared squashfs helpers for any VFS: probe an <see cref="IVirtualFile"/> for a
/// squashfs image and expose its tree as VFS nodes. Reads go through the virtual
/// file, so nested filesystems never require extraction.
/// </summary>
public static class VfsSquashfs
{
    public const uint HsqsMagic = 0x73717368;

    public static bool TryOpen(
        IVirtualFile image, ICollection<object> keepAlive, out SquashFileSystemReader reader)
    {
        reader = null!;
        try
        {
            using var probe = image.OpenRead();
            var magic = new byte[4];
            if (probe.Read(magic, 0, 4) != 4) return false;
            if (BitConverter.ToUInt32(magic, 0) != HsqsMagic) return false;

            var stream = image.OpenRead();
            var rdr = new SquashFileSystemReader(stream);
            // keep the reader (and its stream) alive for the whole mount session
            keepAlive.Add(rdr);
            reader = rdr;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void BuildTree(IVfsNode vfsNode, SquashFileSystemReader rdr)
    {
        var gate = new object();
        BuildTree(vfsNode, rdr.Root, gate);
    }

    private static void BuildTree(IVfsNode vfsNode, DiscDirectoryInfo dirNode, object gate)
    {
        foreach (var d in dirNode.GetDirectories())
        {
            var subdir = new SquashfsDirNode(d.Name, Helpers.OctalLiteral(0755));
            vfsNode.AddNode(subdir);
            BuildTree(subdir, d, gate);
        }
        foreach (var f in dirNode.GetFiles())
        {
            vfsNode.AddNode(new SquashfsFileNode(f, Helpers.OctalLiteral(0666), gate));
        }
    }

    /// <summary>
    /// Mounts one virtual file plus its squashfs contents (if any) under parent.
    /// Returns the number of top-level nodes added (1, or 2 with a squashfs subdir).
    /// </summary>
    public static int AddImage(
        IVfsNode parent, IVirtualFile image, Func<IVirtualFile, VfsNode> makeFile,
        ICollection<object> keepAlive)
    {
        parent.AddNode(makeFile(image));
        if (TryOpen(image, keepAlive, out var reader))
        {
            var mountPoint = new YafexDirectory(
                Path.GetFileNameWithoutExtension(image.Name),
                Helpers.OctalLiteral(0755)
            );
            parent.AddNode(mountPoint);
            BuildTree(mountPoint, reader);
            return 2;
        }
        return 1;
    }
}
