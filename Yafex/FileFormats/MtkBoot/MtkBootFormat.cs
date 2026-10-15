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

namespace Yafex.FileFormats.MtkBoot;

public record MtkBootDetectionResult(int Confidence, bool IsPbl) : DetectionResult(Confidence);

public class MtkBootDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < MtkBoot.PblSize)
            {
                return DetectionResult.Fail;
            }
            var bytes = data.ToArray();
            if (!MtkBoot.HasMagic(bytes))
            {
                return DetectionResult.Fail;
            }
            return new MtkBootDetectionResult(90, MtkBoot.IsPbl(bytes));
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class MtkBootExtractor : IFormatExtractor
{
    private readonly MtkBootDetectionResult _result;

    public MtkBootExtractor(MtkBootDetectionResult result)
    {
        _result = result;
    }

    public System.Collections.Generic.IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var bytes = source.Data.ToArray();
        int firstSize = MtkBoot.FirstStageSize(bytes);
        var first = new byte[firstSize];
        Buffer.BlockCopy(bytes, 0, first, 0, firstSize);
        yield return ArtifactBuilder.Flat(source, "mtk_1bl.bin", first);
        foreach (var s in MtkBoot.Split(bytes))
        {
            var artifact = ArtifactBuilder.Flat(source, s.Name + ".unlzhs", s.Decoded);
            artifact.Flags |= DataSourceFlags.ProcessFurther;
            yield return artifact;
        }
    }
}
