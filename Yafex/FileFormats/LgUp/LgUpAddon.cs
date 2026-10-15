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

public class LgUpAddon : IFormatAddon<LgUpDetectionResult>
{
    public FileFormat FileFormat => FileFormat.LgUp;

    private readonly KeysRepository _keys;

    public LgUpAddon(KeysRepository keys)
    {
        _keys = keys;
    }

    public IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new LgUpDetector(_keys, args);
    }

    public IDictionary<string, string> DescribeOptions() => new Dictionary<string, string>
    {
        { "key", "AES key as 16/24/32 bytes of hex (tried before the lgup-keys bundle)" }
    };

    public IFormatExtractor CreateExtractor(LgUpDetectionResult result)
    {
        if (result.Context == null) throw new ArgumentNullException(nameof(result.Context));
        return new LgUpExtractor(result.Context);
    }

    public IFormatLister? CreateLister(DetectionResult result)
    {
        return result is LgUpDetectionResult typed && typed.Context != null
            ? new LgUpLister(typed)
            : null;
    }

    public YafexVfs? CreateVfs(IDataSource source, DetectionResult result)
    {
        return result is LgUpDetectionResult typed && typed.Context != null
            ? new LgUpVfs(typed.Context, source)
            : null;
    }
}
