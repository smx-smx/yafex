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

namespace Yafex.FileFormats.IntelHex;

public record IntelHexDetectionResult(int Confidence, IntelHex.Image Image) : DetectionResult(Confidence);

public class IntelHexDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 32)
            {
                return DetectionResult.Fail;
            }
            var bytes = data.ToArray();
            if (!IntelHex.TryParse(bytes, out var image))
            {
                return DetectionResult.Fail;
            }
            return new IntelHexDetectionResult(90, image);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class IntelHexLister : IFormatLister
{
    private readonly IntelHexDetectionResult _result;

    public IntelHexLister(IntelHexDetectionResult result)
    {
        _result = result;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        var img = _result.Image;
        long total = 0;
        foreach (var s in img.Segments) total += s.Data.Length;
        output.WriteLine($"IntelHex (micom firmware, {source.Name}):");
        output.WriteLine($"  header={img.HeaderCode}:{img.HeaderPayload} segments={img.Segments.Count} bytes={total}" +
            (img.HasStartAddress ? $" start=0x{img.StartAddress:X}" : ""));
        foreach (var s in img.Segments)
        {
            output.WriteLine($"  0x{s.Start:X}-0x{s.Start + s.Data.Length:X} ({s.Data.Length})");
        }
    }
}

public class IntelHexExtractor : IFormatExtractor
{
    private readonly IntelHexDetectionResult _result;

    public IntelHexExtractor(IntelHexDetectionResult result)
    {
        _result = result;
    }

    public System.Collections.Generic.IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var flat = IntelHex.ToImage(_result.Image, out _);
        var baseName = source.Name ?? "micom";
        foreach (var ext in new[] { ".pak", ".hex", ".lr" })
        {
            if (baseName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName[..^ext.Length];
                break;
            }
        }
        // raw MCU image: leaf, no further processing
        yield return ArtifactBuilder.Flat(source, baseName + ".bin", flat);
    }
}
