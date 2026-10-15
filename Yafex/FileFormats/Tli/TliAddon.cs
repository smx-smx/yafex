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

using Yafex.FileFormats.Tar;
using Yafex.Fuse;
using Yafex.Support;

namespace Yafex.FileFormats.Tli;

public class TliAddon : FormatAddon<TliDetectionResult>
{
    private readonly KeysRepository _keys;

    public TliAddon(KeysRepository keys)
    {
        _keys = keys;
    }

    public override FileFormat FileFormat => FileFormat.Tli;

    public override IDictionary<string, string> DescribeOptions() => new Dictionary<string, string>
    {
        { "password", "TLI image password as text (tried before the tli-password bundle entry)" },
    };

    public override IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new TliDetector(_keys, args);
    }

    protected override bool IsUsable(TliDetectionResult result) => result.Image != null;

    protected override IFormatExtractor CreateExtractor(TliDetectionResult typed)
    {
        return new TliExtractor(typed);
    }

    protected override IFormatLister? CreateLister(TliDetectionResult typed)
    {
        return new TliLister(typed);
    }

    // Lazy mount: one range view per tar-archived image (no copies),
    // mirroring the extractor.
    public override YafexVfs? CreateVfs(IDataSource input, DetectionResult result)
    {
        if (result is not TliDetectionResult typed || typed.Image == null)
        {
            return null;
        }
        try
        {
            VfsMount.TryRoot(input, "tli", out var vfs, out var root);
            var keepAlive = new List<object>();
            using var stream = new System.IO.MemoryStream(
                typed.Image, (int)typed.TarStart, typed.Image.Length - (int)typed.TarStart, false);
            foreach (var e in new YafexTarReader(stream).Entries)
            {
                string name = Support.PathNames.Flatten(e.Name);
                if (name.Length == 0)
                {
                    continue;
                }
                VfsMount.AddRange(root, input, name, typed.TarStart + e.Offset, e.Length,
                    $"tli image {e.Name}", keepAlive);
            }
            return vfs;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
