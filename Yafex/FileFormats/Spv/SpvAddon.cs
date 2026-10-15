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

public class SpvAddon : FormatAddon<SpvDetectionResult>
{
    public override FileFormat FileFormat => FileFormat.Spv;

    public override IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new SpvDetector();
    }

    protected override bool IsUsable(SpvDetectionResult result) => result.Image != null;

    protected override IFormatExtractor CreateExtractor(SpvDetectionResult typed)
    {
        return new SpvExtractor(typed);
    }

    // Lazy mount: one range view per populated bin (no copies),
    // mirroring the extractor.
    public override YafexVfs? CreateVfs(IDataSource input, DetectionResult result)
    {
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
                string name = Support.PathNames.Flatten(b.Name, "bin") + ".bin";
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
