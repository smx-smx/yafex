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

namespace Yafex.FileFormats.LgUp;

public class LgUpAddon : FormatAddon<LgUpDetectionResult>
{
    public override FileFormat FileFormat => FileFormat.LgUp;

    private readonly KeysRepository _keys;

    public LgUpAddon(KeysRepository keys)
    {
        _keys = keys;
    }

    public override IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new LgUpDetector(_keys, args);
    }

    protected override bool IsUsable(LgUpDetectionResult result) => result.Context != null;

    public override IDictionary<string, string> DescribeOptions() => new Dictionary<string, string>
    {
        { "key", "AES key as 16/24/32 bytes of hex (tried before the lgup-keys bundle)" }
    };

    protected override IFormatExtractor CreateExtractor(LgUpDetectionResult result)
    {
        return new LgUpExtractor(result.Context!);
    }

    protected override IFormatLister? CreateLister(LgUpDetectionResult typed)
    {
        return new LgUpLister(typed);
    }

    public override YafexVfs? CreateVfs(IDataSource source, DetectionResult result)
    {
        return result is LgUpDetectionResult typed && typed.Context != null
            ? new LgUpVfs(typed.Context, source)
            : null;
    }
}
