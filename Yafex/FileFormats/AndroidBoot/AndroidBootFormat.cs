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

namespace Yafex.FileFormats.AndroidBoot;

public record AndroidBootDetectionResult(int Confidence, AndroidBoot.Header Header) : DetectionResult(Confidence);

public class AndroidBootDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < AndroidBoot.HeaderV0Size)
            {
                return DetectionResult.Fail;
            }
            if (!AndroidBoot.TryReadHeader(data.ToArray(), out var header))
            {
                return DetectionResult.Fail;
            }
            return new AndroidBootDetectionResult(90, header);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class AndroidBootExtractor : IFormatExtractor
{
    private readonly AndroidBootDetectionResult _result;

    public AndroidBootExtractor(AndroidBootDetectionResult result)
    {
        _result = result;
    }

    public System.Collections.Generic.IEnumerable<IDataSource> Extract(IDataSource source)
    {
        foreach (var seg in _result.Header.Segments)
        {
            if (seg.Offset < 0 || seg.Size <= 0 || seg.Offset + seg.Size > source.Data.Length)
            {
                throw new InvalidDataException($"android boot segment {seg.Name} out of range");
            }
            var bytes = source.Data.Slice(seg.Offset, seg.Size).ToArray();
            // Qualcomm-style appended DTB lives inside kernel_size (e.g.
            // edk-fajita: gzip + 557360 DTB); split it so the gzip part stays
            // Gzip-valid (exact end) and the DTB is preserved as its own blob.
            if (seg.Name == "kernel")
            {
                long dtbAt = AndroidBoot.FindAppendedDtb(bytes);
                if (dtbAt > 0 && AndroidBoot.TryReadDtbSize(bytes, (int)dtbAt, out uint dtbTotal))
                {
                    var kpart = new byte[dtbAt];
                    Buffer.BlockCopy(bytes, 0, kpart, 0, kpart.Length);
                    var kArtifact = ArtifactBuilder.Flat(source, "kernel.bin", kpart);
                    kArtifact.Flags |= DataSourceFlags.Output | DataSourceFlags.ProcessFurther;
                    yield return kArtifact;

                    // one or more concatenated DTBs (each with its own totalsize)
                    int pos = (int)dtbAt;
                    int idx = 0;
                    while (pos < bytes.Length
                        && AndroidBoot.TryReadDtbSize(bytes, pos, out uint total))
                    {
                        var dtb = new byte[total];
                        Buffer.BlockCopy(bytes, pos, dtb, 0, dtb.Length);
                        var name = idx == 0 ? "kernel.dtb" : $"kernel.dtb{idx}";
                        var dArtifact = ArtifactBuilder.Flat(source, name, dtb);
                        dArtifact.Flags |= DataSourceFlags.Output | DataSourceFlags.ProcessFurther;
                        yield return dArtifact;
                        pos += (int)total;
                        idx++;
                    }
                    continue;
                }
            }
            var artifact = ArtifactBuilder.Flat(source, seg.Name + ".bin", bytes);
            artifact.Flags |= DataSourceFlags.Output | DataSourceFlags.ProcessFurther;
            yield return artifact;
        }
    }
}
