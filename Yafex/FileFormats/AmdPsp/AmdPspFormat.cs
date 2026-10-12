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

namespace Yafex.FileFormats.AmdPsp;

public record AmdPspDetectionResult(
    int Confidence,
    List<AmdPsp.Directory> Directories,
    List<AmdPsp.Efs> EfsList) : DetectionResult(Confidence);

public class AmdPspDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < AmdPsp.DirHeaderSize + AmdPsp.PspEntrySize)
            {
                return DetectionResult.Fail;
            }
            var bytes = data.ToArray();
            var dirs = AmdPsp.FindDirectories(bytes);
            if (dirs.Count == 0)
            {
                return DetectionResult.Fail;
            }
            var efs = AmdPsp.FindEfs(bytes);
            // EFS signature plus pointers resolving to validated directories
            // is the strongest evidence (beats magic-only 90s on ties).
            int confidence = efs.Count > 0 ? 95 : 90;
            return new AmdPspDetectionResult(confidence, dirs, efs);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}
