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

namespace Yafex.FileFormats.Idb;

public record IdbDetectionResult(int Confidence, Idb.Table Table) : DetectionResult(Confidence);

public class IdbDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 64)
            {
                return DetectionResult.Fail;
            }
            var bytes = data.ToArray();
            if (!Idb.TryParse(bytes, out var table))
            {
                return DetectionResult.Fail;
            }
            return new IdbDetectionResult(90, table);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class IdbLister : IFormatLister
{
    private readonly IdbDetectionResult _result;

    public IdbLister(IdbDetectionResult result)
    {
        _result = result;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        var table = _result.Table;
        long payload = 0;
        int live = 0;
        foreach (var e in table.Entries)
        {
            if (e.Size > 0)
            {
                live++;
                payload += e.Size;
            }
        }
        output.WriteLine($"Idb (image database, {source.Name}):");
        output.WriteLine($"  images={table.ImageCount} live={live} payload={payload}");
    }
}

public class IdbExtractor : IFormatExtractor
{
    private readonly IdbDetectionResult _result;

    public IdbExtractor(IdbDetectionResult result)
    {
        _result = result;
    }

    public System.Collections.Generic.IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var bytes = source.Data.ToArray();
        // reparse (cheap): the extractor must not trust a stale table
        if (!Idb.TryParse(bytes, out var table))
        {
            throw new InvalidDataException("not an IDB file");
        }
        // reference parity: sequential numbers over non-skipped entries
        int n = 0;
        foreach (var e in table.Entries)
        {
            if (e.Size == 0)
            {
                continue;
            }
            n++;
            var blob = new byte[e.Size];
            Buffer.BlockCopy(bytes, (int)e.Offset, blob, 0, (int)e.Size);
            yield return ArtifactBuilder.Flat(source, n + ".png", blob);
        }
    }
}
