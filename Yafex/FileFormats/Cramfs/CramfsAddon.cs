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

namespace Yafex.FileFormats.Cramfs;

public class CramfsAddon : IFormatAddon
{
    public FileFormat FileFormat => FileFormat.Cramfs;

    public IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new CramfsDetector();
    }

    public IFormatExtractor CreateExtractor(DetectionResult result)
    {
        if (result is not CramfsDetectionResult typed || typed.Image == null)
        {
            throw new ArgumentException(nameof(result));
        }
        return new CramfsExtractor(typed);
    }

    public IFormatLister? CreateLister(DetectionResult result)
    {
        return result is CramfsDetectionResult typed && typed.Image != null
            ? new CramfsLister(typed)
            : null;
    }

    public IVfsNode CreateVfsNode(IDataSource ds)
    {
        return new YafexFile(ds, Helpers.OctalLiteral(0444));
    }
}
