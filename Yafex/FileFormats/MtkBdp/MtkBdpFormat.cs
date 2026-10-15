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
using System.Linq;

using Yafex.FileSystems.MtkBdp;

namespace Yafex.FileFormats.MtkBdp;

public record MtkBdpDetectionResult(int Confidence, byte[]? Image, List<MtkBdpImage> Parts) : DetectionResult(Confidence);

public class MtkBdpDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 128) return DetectionResult.Fail;
            var bytes = data.ToArray();
            var parts = MtkBdpParser.Open(bytes);
            if (parts == null) return DetectionResult.Fail;
            return new MtkBdpDetectionResult(90, bytes, parts);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class MtkBdpExtractor : IFormatExtractor
{
    private readonly byte[] _image;
    private readonly List<MtkBdpImage> _parts;

    public MtkBdpExtractor(MtkBdpDetectionResult result)
    {
        _image = result.Image!;
        _parts = result.Parts;
    }

    public static string SafeName(string name)
    {
        string flat = name.Replace('/', '_').Replace('\\', '_').Trim();
        return flat.Length == 0 ? "part" : flat;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        foreach (var p in _parts.OrderBy(x => x.Part.Id))
        {
            yield return ArtifactBuilder.Flat(source, SafeName(p.Part.Name) + ".bin", p.Assemble(_image));
        }
    }
}
