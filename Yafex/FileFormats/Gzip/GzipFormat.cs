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

namespace Yafex.FileFormats.Gzip;

public record GzipDetectionResult(int Confidence, Gzip.Member Member) : DetectionResult(Confidence);

public class GzipDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 18)
            {
                return DetectionResult.Fail;
            }
            if (!Gzip.TryReadMember(data.ToArray(), out var member))
            {
                return DetectionResult.Fail;
            }
            return new GzipDetectionResult(90, member);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class GzipExtractor : IFormatExtractor
{
    private readonly GzipDetectionResult _result;

    public GzipExtractor(GzipDetectionResult result)
    {
        _result = result;
    }

    public System.Collections.Generic.IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var member = _result.Member;
        var outName = SaneFileName(member.FileName) ?? ((source.Name ?? "gzip") + ".ungz");
        var artifact = ArtifactBuilder.Flat(source, outName, member.Payload);
        artifact.Flags |= DataSourceFlags.ProcessFurther;
        yield return artifact;
    }

    internal static string? SaneFileName(string name)
    {
        if (name.Length == 0 || name.Length > 64)
        {
            return null;
        }
        foreach (char c in name)
        {
            if (c < 0x20 || c >= 0x7F || c == '/' || c == '\\')
            {
                return null;
            }
        }
        return name;
    }
}
