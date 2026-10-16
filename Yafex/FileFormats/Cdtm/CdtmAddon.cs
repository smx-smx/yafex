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

namespace Yafex.FileFormats.Cdtm;

public class CdtmAddon : FormatAddon<CdtmDetectionResult>
{
    public override FileFormat FileFormat => FileFormat.Cdtm;

    public override IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new CdtmDetector();
    }

    protected override bool IsUsable(CdtmDetectionResult result) => result.Entries.Count > 0;

    protected override IFormatExtractor CreateExtractor(CdtmDetectionResult typed)
    {
        return new CdtmExtractor(typed);
    }

    public override YafexVfs? CreateVfs(IDataSource input, DetectionResult result)
    {
        if (result is not CdtmDetectionResult typed) return null;
        try
        {
            VfsMount.TryRoot(input, Support.PathNames.Flatten(typed.FileName), out var vfs, out var root);
            var keepAlive = new List<object>();
            foreach (var entry in typed.Entries)
            {
                VfsMount.AddRange(root, input, CdtmExtractor.SafeName(entry.Name) + ".bin",
                    entry.Offset, entry.Size, $"cdtm {entry.Name}", keepAlive);
            }
            return vfs;
        }
        catch (System.Exception)
        {
            return null;
        }
    }
}
