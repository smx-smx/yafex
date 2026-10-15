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
using System.IO;

namespace Yafex.FileFormats.Lz4;

public record Lz4pDetectionResult(int Confidence, int ChunkSize, int BlockCount, long OutputSize) : DetectionResult(Confidence);

public class Lz4pDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < Lz4p.HeaderSize)
            {
                return DetectionResult.Fail;
            }
            var bytes = data.ToArray();
            if (!Lz4p.TryReadArchive(bytes, out var archive))
            {
                return DetectionResult.Fail;
            }
            // LZ4 decodes GB/s: trial-decode everything for a strong verdict.
            var decoded = Lz4p.Decode(bytes);
            return new Lz4pDetectionResult(90, archive.ChunkSize, archive.BlockCount, decoded.Length);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class Lz4pExtractor : IFormatExtractor
{
    public Lz4pExtractor(Lz4pDetectionResult result)
    {
    }

    public System.Collections.Generic.IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var decoded = Lz4p.Decode(source.Data.ToArray());
        var baseName = source.Name ?? "lz4p";
        var outName = baseName + ".unlz4";
        var artifact = ArtifactBuilder.Flat(source, outName, decoded);
        artifact.Flags |= DataSourceFlags.ProcessFurther;
        yield return artifact;
    }
}
