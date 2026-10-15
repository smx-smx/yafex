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

namespace Yafex.FileFormats.Cramfs;

public record CramfsDetectionResult(int Confidence, byte[]? Image, bool BigEndian = false) : DetectionResult(Confidence);

public class CramfsDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 76) return DetectionResult.Fail;
            var prefix = data.Slice(0, 32).ToArray();
            if (!CramfsReader.HasMagic(prefix, out bool bigEndian)) return DetectionResult.Fail;
            // full validation (signature, root): needs the whole image
            var bytes = data.ToArray();
            var reader = new CramfsReader(bytes, bigEndian);
            return new CramfsDetectionResult(90, bytes, bigEndian);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class CramfsExtractor : IFormatExtractor
{
    private readonly byte[] _image;
    private readonly bool _bigEndian;

    public CramfsExtractor(CramfsDetectionResult result)
    {
        _image = result.Image!;
        _bigEndian = result.BigEndian;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var reader = new CramfsReader(_image, _bigEndian);
        foreach (var (path, content) in Walk(reader, reader.Root, ""))
        {
            // flat output: hierarchy stays visible via --list/--tree and mount.
            // (A subdir per image would collide with the image file itself,
            // e.g. "lgap.unlzo/" next to "lgap.unlzo".)
            var flat = path.Replace('/', '_');
            yield return ArtifactBuilder.Flat(source, flat, content);
        }
    }

    private static IEnumerable<(string Path, byte[] Content)> Walk(
        CramfsReader reader, CramfsInode dir, string prefix)
    {
        foreach (var e in reader.ReadDirectory(dir))
        {
            var path = prefix + e.Name;
            if (e.IsDirectory)
            {
                foreach (var sub in Walk(reader, e, path + "/"))
                {
                    yield return sub;
                }
            }
            else if (e.IsRegular)
            {
                yield return (path, reader.ReadFile(e));
            }
            else if (e.IsSymlink)
            {
                yield return (path, System.Text.Encoding.ASCII.GetBytes(reader.ReadSymlink(e)));
            }
        }
    }
}
