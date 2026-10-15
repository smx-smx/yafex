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

using Yafex.FileFormats.Uefi;

using UefiParser = Yafex.FileFormats.Uefi.Uefi;

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

public class AmdPspExtractor : IFormatExtractor
{
    private readonly AmdPspDetectionResult _result;

    public AmdPspExtractor(AmdPspDetectionResult result)
    {
        _result = result;
    }

    public static bool Overlaps(long off, long len, long o2, long l2)
    {
        return off < o2 + l2 && o2 < off + len;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var bytes = source.Data.ToArray();
        // UEFI firmware volumes fully supersede overlapping BIOS entries
        // (e.g. 0x62 reset image / 0x6d NV store slices starting mid-FV);
        // skip those so the FV slices below stay the single covering view.
        var volumes = UefiParser.FindVolumes(bytes);
        for (int di = 0; di < _result.Directories.Count; di++)
        {
            var dir = _result.Directories[di];
            string prefix = dir.Kind == AmdPsp.DirKind.Psp ? "psp" : "bios";
            for (int i = 0; i < dir.Entries.Count; i++)
            {
                var e = dir.Entries[i];
                if (e.Avail <= 0)
                {
                    continue;
                }
                bool covered = false;
                foreach (var v in volumes)
                {
                    if (Overlaps(e.ResolvedOffset, e.Avail, v.Offset, v.Length))
                    {
                        covered = true;
                        break;
                    }
                }
                if (covered)
                {
                    continue;
                }
                var content = new byte[e.Avail];
                Buffer.BlockCopy(bytes, (int)e.ResolvedOffset, content, 0, content.Length);
                var artifact = ArtifactBuilder.Flat(source,
                    $"{prefix}{di}_{e.TypeName}_{i:00}.bin", content);
                artifact.Flags |= DataSourceFlags.Output | DataSourceFlags.ProcessFurther;
                yield return artifact;
            }
        }
        for (int i = 0; i < volumes.Count; i++)
        {
            var v = volumes[i];
            var content = new byte[v.Length];
            Buffer.BlockCopy(bytes, (int)v.Offset, content, 0, content.Length);
            var artifact = ArtifactBuilder.Flat(source, $"fv_{i:00}.bin", content);
            artifact.Flags |= DataSourceFlags.Output | DataSourceFlags.ProcessFurther;
            yield return artifact;
        }
    }
}
