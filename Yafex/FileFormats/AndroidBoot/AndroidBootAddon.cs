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

using Yafex.Fuse;
using Yafex.Support;

namespace Yafex.FileFormats.AndroidBoot;

public class AndroidBootAddon : IFormatAddon
{
    public FileFormat FileFormat => FileFormat.AndroidBoot;

    public IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new AndroidBootDetector();
    }

    public IFormatExtractor CreateExtractor(DetectionResult result)
    {
        if (result is not AndroidBootDetectionResult typed)
        {
            throw new ArgumentException(nameof(result));
        }
        return new AndroidBootExtractor(typed);
    }

    public IFormatLister? CreateLister(DetectionResult result)
    {
        return result is AndroidBootDetectionResult typed
            ? new AndroidBootLister(typed)
            : null;
    }

    public IVfsNode CreateVfsNode(IDataSource ds)
    {
        return new YafexFile(ds, Helpers.OctalLiteral(0444));
    }

    // Lazy mount: header + each segment as a range view (no copies);
    // kernel-embedded DTB (e.g. edk-fajita) gets its own view too.
    public YafexVfs? CreateVfs(IDataSource source, DetectionResult result)
    {
        if (source is not MFile input) return null;
        if (result is not AndroidBootDetectionResult typed) return null;
        try
        {
            var header = typed.Header;
            VfsMount.TryRoot(input, "androidboot", out var vfs, out var root);
            var keepAlive = new List<object>();
            foreach (var seg in header.Segments)
            {
                if (seg.Name == "kernel"
                    && seg.Size <= int.MaxValue
                    && seg.Offset + seg.Size <= input.Data.Length)
                {
                    var kbytes = input.Data.Slice(seg.Offset, seg.Size).ToArray();
                    long dtbAt = AndroidBoot.FindAppendedDtb(kbytes);
                    if (dtbAt > 0)
                    {
                        VfsMount.AddRange(root, input, "kernel.bin",
                            seg.Offset, dtbAt,
                            $"android boot v{header.HeaderVersion} kernel (gzip part)",
                            keepAlive);
                        int pos = (int)dtbAt;
                        int idx = 0;
                        while (pos < kbytes.Length
                            && AndroidBoot.TryReadDtbSize(kbytes, pos, out uint total))
                        {
                            var name = idx == 0 ? "kernel.dtb" : $"kernel.dtb{idx}";
                            VfsMount.AddRange(root, input, name,
                                seg.Offset + pos, total,
                                "appended DTB in kernel",
                                keepAlive);
                            pos += (int)total;
                            idx++;
                        }
                        continue;
                    }
                }
                VfsMount.AddRange(root, input, seg.Name + ".bin",
                    seg.Offset, seg.Size,
                    $"android boot v{header.HeaderVersion} {seg.Name}",
                    keepAlive);
            }
            return vfs;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
