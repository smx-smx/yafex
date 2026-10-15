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
using System.IO;
using System.Linq;

using Yafex.FileSystems.Base;
using Yafex.FileSystems.SquashFs;

namespace Yafex.FileFormats.Squashfs;

// Standalone squashfs 4.x support through the Yafex-native reader.
// Mount expansion already handles 4.x via SquashfsExpander; this covers
// --probe/--list/--extract.

public record Squashfs4DetectionResult(int Confidence, byte[]? Image) : DiscImageDetectionResult(Confidence, Image);

public class Squashfs4Detector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 32) return DetectionResult.Fail;
            var prefix = data.Slice(0, 32).ToArray();
            if (BitConverter.ToUInt32(prefix, 0) != 0x73717368) return DetectionResult.Fail;
            if (BitConverter.ToUInt16(prefix, 28) != 4) return DetectionResult.Fail;
            var bytes = data.ToArray();
            using var stream = new MemoryStream(bytes, writable: false);
            using var fs = new SquashFileSystemReader(stream);
            if (fs.Root == null) return DetectionResult.Fail;
            return new Squashfs4DetectionResult(90, bytes);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}
