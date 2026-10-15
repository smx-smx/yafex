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

namespace Yafex.FileFormats.Nfsb;

public record NfsbDetectionResult(int Confidence, long InnerSize) : DetectionResult(Confidence);

public class NfsbDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length <= Nfsb.HeaderSize)
            {
                return DetectionResult.Fail;
            }
            var prefix = data.Slice(0, Math.Min(data.Length, 64)).ToArray();
            if (!Nfsb.HasMagic(prefix))
            {
                return DetectionResult.Fail;
            }
            return new NfsbDetectionResult(90, data.Length - Nfsb.HeaderSize);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class NfsbExtractor : IFormatExtractor
{
    public NfsbExtractor(NfsbDetectionResult result)
    {
    }

    public System.Collections.Generic.IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var bytes = source.Data.ToArray();
        var inner = Nfsb.Strip(bytes);
        var baseName = source.Name ?? "nfsb";
        var outName = baseName + ".unnfsb";
        var artifact = ArtifactBuilder.Flat(source, outName, inner);
        artifact.Flags |= DataSourceFlags.ProcessFurther;
        yield return artifact;
    }
}
