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

namespace Yafex.FileFormats.Msd11;

public class Msd11Addon : FormatAddon<Msd11DetectionResult>
{
    public override FileFormat FileFormat => FileFormat.Msd11;

    private readonly KeysRepository _keys;

    public Msd11Addon(KeysRepository keys)
    {
        _keys = keys;
    }

    public override IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new Msd11Detector(_keys, args);
    }

    protected override bool IsUsable(Msd11DetectionResult result) => result.Context != null;

    public override IDictionary<string, string> DescribeOptions() => new Dictionary<string, string>
    {
        { "key", "TOC/member passphrase as 16/32 bytes of hex (tried before the MSD11 bundle)" }
    };

    protected override IFormatExtractor CreateExtractor(Msd11DetectionResult typed)
    {
        return new Msd11Extractor(typed);
    }

    public override YafexVfs? CreateVfs(IDataSource input, DetectionResult result)
    {
        if (result is not Msd11DetectionResult r || r.Context == null) return null;
        try
        {
            return Msd11Vfs.Create(input, r.Context);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
