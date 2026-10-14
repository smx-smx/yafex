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
using System.Text;

using Yafex.Fuse;
using Yafex.Support;

namespace Yafex.FileFormats.Spv;

public class SpvAddon : IFormatAddon
{
    public FileFormat FileFormat => FileFormat.Spv;

    public IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new SpvDetector();
    }

    public IFormatExtractor CreateExtractor(DetectionResult result)
    {
        if (result is not SpvDetectionResult typed || typed.Image == null)
        {
            throw new ArgumentException(nameof(result));
        }
        return new SpvExtractor(typed);
    }

    public IFormatLister? CreateLister(DetectionResult result)
    {
        return result is SpvDetectionResult typed && typed.Image != null
            ? new SpvLister(typed)
            : null;
    }

    public IVfsNode CreateVfsNode(IDataSource ds)
    {
        return new YafexFile(ds, Helpers.OctalLiteral(0444));
    }

    // Lazy mount: one range view per populated bin (no copies),
    // mirroring the extractor.
    public YafexVfs? CreateVfs(IDataSource source, DetectionResult result)
    {
        if (source is not MFile input)
        {
            return null;
        }
        if (result is not SpvDetectionResult typed || typed.Image == null)
        {
            return null;
        }
        try
        {
            VfsMount.TryRoot(input, "spv", out var vfs, out var root);
            var keepAlive = new List<object>();
            foreach (var b in typed.Bins)
            {
                string name = SpvParser.SafeName(b.Name) + ".bin";
                long length = SpvParser.PayloadLength(typed.Image, b);
                if (length <= 0) continue;
                VfsMount.AddRange(root, input, name, b.Offset, length,
                    $"spv bin {b.Name} addr=0x{b.Addr:X} checksum=0x{b.Checksum:X8}", keepAlive);
            }
            return vfs;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
