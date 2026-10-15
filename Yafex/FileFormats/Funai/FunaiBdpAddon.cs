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
using System.Collections.Generic;

using Yafex.Fuse;

namespace Yafex.FileFormats.Funai;

public class FunaiBdpAddon : FormatAddon<FunaiBdpDetectionResult>
{
    public override FileFormat FileFormat => FileFormat.FunaiBdp;

    private readonly KeysRepository _keys;

    public FunaiBdpAddon(KeysRepository keys)
    {
        _keys = keys;
    }

    public override IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new FunaiBdpDetector(_keys, args);
    }

    protected override bool IsUsable(FunaiBdpDetectionResult result) => result.Index.Count > 0;

    protected override IFormatExtractor CreateExtractor(FunaiBdpDetectionResult result)
    {
        return new FunaiBdpExtractor(result);
    }

    public override IDictionary<string, string> DescribeOptions() => new Dictionary<string, string>
    {
        { "key", "32-bit Funai key as 8 hex chars (4 bytes LE), tried before the funai-bdp-keys bundle" },
    };

    public override YafexVfs? CreateVfs(IDataSource source, DetectionResult result)
    {
        if (result is not FunaiBdpDetectionResult typed) return null;
        try
        {
            VfsMount.TryRoot(source, "funai_bdp", out var vfs, out var root);
            var keepAlive = new List<object>();
            foreach (var entry in typed.Index)
            {
                var file = new FunaiBdpPartitionFile(
                    FunaiBdpExtractor.SafeName(entry.Name) + ".bin",
                    source, entry.Offset, entry.Size, typed.Key);
                VfsBuilder.AddImage(root, file,
                    VfsImageViews.Decrypted | VfsImageViews.Metadata | VfsImageViews.Expanded,
                    keepAlive);
            }
            return vfs;
        }
        catch (System.Exception)
        {
            return null;
        }
    }
}
