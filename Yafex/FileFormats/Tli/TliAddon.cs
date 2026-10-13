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

using DiscUtils.Archives;

using Yafex.Fuse;
using Yafex.Support;

namespace Yafex.FileFormats.Tli;

public class TliAddon : IFormatAddon
{
    private readonly KeysRepository _keys;

    public TliAddon(KeysRepository keys)
    {
        _keys = keys;
    }

    public FileFormat FileFormat => FileFormat.Tli;

    public IDictionary<string, string> DescribeOptions() => new Dictionary<string, string>
    {
        { "password", "TLI image password as text (tried before the tli-password bundle entry)" },
    };

    public IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new TliDetector(_keys, args);
    }

    public IFormatExtractor CreateExtractor(DetectionResult result)
    {
        if (result is not TliDetectionResult typed || typed.Image == null)
        {
            throw new ArgumentException(nameof(result));
        }
        return new TliExtractor(typed);
    }

    public IFormatLister? CreateLister(DetectionResult result)
    {
        return result is TliDetectionResult typed && typed.Image != null
            ? new TliLister(typed)
            : null;
    }

    public IVfsNode CreateVfsNode(IDataSource ds)
    {
        return new YafexFile(ds, Helpers.OctalLiteral(0444));
    }

    // Lazy mount: one range view per tar-archived image (no copies),
    // mirroring the extractor.
    public YafexVfs? CreateVfs(IDataSource source, DetectionResult result)
    {
        if (source is not MFile input)
        {
            return null;
        }
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
            foreach (var e in new TarFile(stream).Entries)
            {
                string name = TliParser.SafeName(e.Name);
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
