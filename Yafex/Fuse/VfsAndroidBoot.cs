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

using Yafex.FileFormats.AndroidBoot;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for Android boot images (boot.img/recovery.img v0/v1/v2):
/// exposes kernel/ramdisk/second/recovery_dtbo/dtb slices plus
/// kernel-embedded DTB (e.g. edk-fajita), chaining each into the
/// nested-format expander (gzip kernel, etc.) on access.
/// </summary>
public sealed class AndroidBootExpander : IContentExpander
{
    public string Name => "androidboot";

    public bool Matches(IVirtualFile file)
    {
        return file.Length >= AndroidBoot.HeaderV0Size
            && ExpanderSniff.Head(file, 8, AndroidBoot.HasMagic);
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            var nodes = new List<IVfsNode>();
            // full image needed for layout (segments reference file offsets)
            byte[] image = file.ReadAvailable();
            if (!AndroidBoot.TryReadHeader(image, out var header))
            {
                throw new InvalidOperationException("android boot header parse failed");
            }
            void AddSlice(string name, long offset, long size, string? meta)
            {
                var slice = new SliceVirtualFile(name, file, offset, size);
                VfsBuilder.AddWithNested(nodes, slice, keepAlive);
            }
            foreach (var seg in header.Segments)
            {
                if (seg.Name == "kernel" && seg.Size <= int.MaxValue)
                {
                    var kbytes = new byte[seg.Size];
                    Buffer.BlockCopy(image, (int)seg.Offset, kbytes, 0, kbytes.Length);
                    long dtbAt = AndroidBoot.FindAppendedDtb(kbytes);
                    if (dtbAt > 0)
                    {
                        AddSlice("kernel.bin", seg.Offset, dtbAt,
                            $"android boot v{header.HeaderVersion} kernel (gzip part)");
                        int pos = (int)dtbAt;
                        int idx = 0;
                        while (pos < kbytes.Length
                            && AndroidBoot.TryReadDtbSize(kbytes, pos, out uint total))
                        {
                            var name = idx == 0 ? "kernel.dtb" : $"kernel.dtb{idx}";
                            AddSlice(name, seg.Offset + pos, total, "appended DTB in kernel");
                            pos += (int)total;
                            idx++;
                        }
                        continue;
                    }
                }
                AddSlice(seg.Name + ".bin", seg.Offset, seg.Size,
                    $"android boot v{header.HeaderVersion} {seg.Name}");
            }
            return nodes;
        });
    }
}
