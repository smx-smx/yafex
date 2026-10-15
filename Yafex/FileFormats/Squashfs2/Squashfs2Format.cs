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
using Yafex.Support;
using Smx.SharpIO.Memory.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Yafex.FileSystems.Base;
using Yafex.FileSystems.Squashfs2;

namespace Yafex.FileFormats.Squashfs2;

public record Squashfs2DetectionResult(int Confidence, byte[]? Image, long Offset, bool BigEndian = false) : DiscImageDetectionResult(Confidence, Image, Offset);

public class Squashfs2Detector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 119) return DetectionResult.Fail;
            // embedded images (e.g. SerComm firmware): scan for the magic,
            // validating each candidate through a slice of the source
            foreach (long off in FindCandidates(data))
            {
                try
                {
                    using (var stream = new Yafex.Fuse.DataSourceRangeStream(source, off, data.Length - off))
                    using (var fs = new Squashfs2FileSystem(stream))
                    {
                        if (fs.Root == null) continue;
                    }
                    var bytes = data.ToArray();
                    bool bigEndian = bytes[off] == (byte)'s' && bytes[off + 1] == (byte)'q';
                    return new Squashfs2DetectionResult(90, bytes, off, bigEndian);
                }
                catch (Exception)
                {
                    continue;
                }
            }
            return DetectionResult.Fail;
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }

    /// <summary>First 16 offsets holding either endianness of the magic, ascending.</summary>
    internal static IEnumerable<long> FindCandidates(Memory64<byte> data)
    {
        var le = BitConverter.GetBytes(Squashfs2FileSystem.Magic);
        var be = BitConverter.GetBytes(0x68737173u);
        var hits = new List<long>();
        hits.AddRange(System.Linq.Enumerable.Take(DataScan.FindAll(data, le), 16));
        hits.AddRange(System.Linq.Enumerable.Take(DataScan.FindAll(data, be), 16));
        hits.Sort();
        return System.Linq.Enumerable.Take(hits, 16);
    }
}
