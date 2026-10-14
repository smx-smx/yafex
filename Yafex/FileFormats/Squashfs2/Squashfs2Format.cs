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

using Yafex.FileSystems.Base;
using Yafex.FileSystems.Squashfs2;

namespace Yafex.FileFormats.Squashfs2;

public record Squashfs2DetectionResult(int Confidence, byte[]? Image, long Offset, bool BigEndian = false) : DetectionResult(Confidence);

public class Squashfs2Detector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 119) return DetectionResult.Fail;
            var bytes = data.ToArray();
            // embedded images (e.g. SerComm firmware): scan for the magic
            foreach (long off in FindCandidates(bytes))
            {
                try
                {
                    using var stream = new MemoryStream(bytes, (int)off, bytes.Length - (int)off, writable: false);
                    using var fs = new Squashfs2FileSystem(stream);
                    var root = fs.Root;
                    if (root == null) continue;
                    bool bigEndian = bytes[off] == (byte)'s' && bytes[off + 1] == (byte)'q';
                    return new Squashfs2DetectionResult(90, bytes, off, bigEndian);
                }
                catch (Exception)
                {
                    continue;
                }
            }
            return DetectionResult.Fail;
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }

    internal static IEnumerable<long> FindCandidates(byte[] image)
    {
        int found = 0;
        for (long i = 0; i + 4 <= image.Length && found < 16; i++)
        {
            uint magic = (uint)(image[i] | (image[i + 1] << 8) | (image[i + 2] << 16) | (image[i + 3] << 24));
            if (magic == Squashfs2FileSystem.Magic || magic == 0x68737173)
            {
                yield return i;
                found++;
            }
        }
    }
}

public class Squashfs2Lister : IFormatLister
{
    private readonly byte[] _image;
    private readonly long _offset;

    public Squashfs2Lister(Squashfs2DetectionResult result)
    {
        _image = result.Image!;
        _offset = result.Offset;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        using var stream = new MemoryStream(_image, (int)_offset, _image.Length - (int)_offset, writable: false);
        using var fs = new Squashfs2FileSystem(stream);
        output.WriteLine($"Squashfs2 ({source.Name}, {fs.Root.GetFiles().Length + fs.Root.GetDirectories().Length} top entries):");
        Walk(fs, fs.Root, "", output);
    }

    private static void Walk(Squashfs2FileSystem fs, DiscDirectoryInfo dir, string prefix, TextWriter output)
    {
        foreach (var d in dir.GetDirectories())
        {
            output.WriteLine($"  d {prefix}{d.Name}");
            Walk(fs, d, prefix + d.Name + "/", output);
        }
        foreach (var f in dir.GetFiles())
        {
            output.WriteLine($"  f {prefix}{f.Name} size={f.Length}");
        }
    }
}

public class Squashfs2Extractor : IFormatExtractor
{
    private readonly byte[] _image;
    private readonly long _offset;

    public Squashfs2Extractor(Squashfs2DetectionResult result)
    {
        _image = result.Image!;
        _offset = result.Offset;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        using var stream = new MemoryStream(_image, (int)_offset, _image.Length - (int)_offset, writable: false);
        using var fs = new Squashfs2FileSystem(stream);
        foreach (var (path, content) in Fuse.DiscTree.Flatten(fs))
        {
            // flat output: hierarchy stays visible via --list/--tree and mount
            var flat = path.Replace('/', '_');
            yield return ArtifactBuilder.Flat(source, flat, content);
        }
    }
}
