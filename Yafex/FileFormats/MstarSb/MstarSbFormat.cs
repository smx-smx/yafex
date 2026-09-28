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

namespace Yafex.FileFormats.MstarSb;

public record MstarSbDetectionResult(int Confidence, int Headers, int KeySlots) : DetectionResult(Confidence);

public class MstarSbDetector : IFormatDetector
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
            var headers = MstarSb.FindHeaders(bytes);
            if (headers.Count == 0)
            {
                return DetectionResult.Fail;
            }
            int slots = MstarSb.FindKeySlots(bytes).Count;
            // single headers happen by chance in large binaries; pairs and
            // keyslots are the strong signal
            int confidence = (headers.Count >= 2 || slots > 0) ? 80 : 30;
            return new MstarSbDetectionResult(confidence, headers.Count, slots);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class MstarSbLister : IFormatLister
{
    private readonly MstarSbDetectionResult _result;

    public MstarSbLister(MstarSbDetectionResult result)
    {
        _result = result;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        var bytes = source.Data.ToArray();
        output.WriteLine($"MstarSb (MStar secure boot, {source.Name}):");
        foreach (var h in MstarSb.FindHeaders(bytes))
        {
            output.WriteLine($"  SECURITY @0x{h.Offset:X} frags={h.FragNum} unit={h.FragUnitSize}");
        }
        foreach (var k in MstarSb.FindKeySlots(bytes))
        {
            output.WriteLine($"  keyslot @0x{k.Offset:X} id={k.MagicId}");
        }
    }
}

public class MstarSbExtractor : IFormatExtractor
{
    public MstarSbExtractor(MstarSbDetectionResult result)
    {
    }

    public System.Collections.Generic.IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var bytes = source.Data.ToArray();
        var headers = MstarSb.FindHeaders(bytes);
        // split at every header boundary (header+payload runs)
        for (int i = 0; i < headers.Count; i++)
        {
            long start = headers[i].Offset;
            long end = i + 1 < headers.Count ? headers[i + 1].Offset : bytes.Length;
            if (end <= start)
            {
                continue;
            }
            var blob = new byte[end - start];
            Buffer.BlockCopy(bytes, (int)start, blob, 0, blob.Length);
            yield return ArtifactBuilder.Flat(source, $"sec_{start:X}.bin", blob);
        }
    }
}
