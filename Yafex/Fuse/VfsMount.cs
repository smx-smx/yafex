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

using Yafex.Support;

namespace Yafex.Fuse;

/// <summary>Shared lazy-mount builders: a root directory named after the input
/// plus plaintext range views (decrypted/metadata/expanded, no raw duplicate).
/// Mount walks headers/tables only; payload bytes stay on disk until read.</summary>
public static class VfsMount
{
    public static bool TryRoot(MFile input, string fallback, out YafexVfs vfs, out YafexDirectory root)
    {
        vfs = new YafexVfs();
        root = new YafexDirectory(input.Name ?? fallback, Helpers.OctalLiteral(0755));
        vfs.Root.AddNode(root);
        return true;
    }

    /// <summary>Adds a plaintext window as browsable file (expanded registry
    /// applies, e.g. squashfs inside partitions). Clamps to the input.</summary>
    public static void AddRange(YafexDirectory root, MFile input, string name,
        long offset, long size, string? meta, System.Collections.Generic.List<object> keepAlive)
    {
        if (offset < 0 || offset >= input.Data.Length) return;
        size = Math.Min(size, input.Data.Length - offset);
        if (size <= 0) return;
        var image = new RangeVirtualFile(name, input, offset, size);
        VfsBuilder.AddImage(root, image,
            VfsImageViews.Decrypted | VfsImageViews.Metadata | VfsImageViews.Expanded,
            keepAlive,
            describe: meta != null ? () => meta : null);
    }
}
