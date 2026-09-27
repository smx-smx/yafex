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

using DiscUtils;
using DiscUtils.SquashFs3;

using log4net;

namespace Yafex.FileFormats.Squashfs3;

public record Squashfs3DetectionResult(int Confidence, byte[]? Image, bool BigEndian = false) : DetectionResult(Confidence);

public class Squashfs3Detector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 119) return DetectionResult.Fail;
            var prefix = data.Slice(0, 32).ToArray();
            if (!Squashfs3FileSystem.HasMagic(prefix, out bool bigEndian)) return DetectionResult.Fail;
            ushort major = bigEndian
                ? (ushort)((prefix[28] << 8) | prefix[29])
                : BitConverter.ToUInt16(prefix, 28);
            if (major != Squashfs3FileSystem.MajorVersion) return DetectionResult.Fail;
            var bytes = data.ToArray();
            using var stream = new MemoryStream(bytes, writable: false);
            using var fs = new Squashfs3FileSystem(stream);
            // force the root open while validating
            var root = fs.Root;
            if (root == null) return DetectionResult.Fail;
            return new Squashfs3DetectionResult(90, bytes, bigEndian);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class Squashfs3Lister : IFormatLister
{
    private readonly byte[] _image;

    public Squashfs3Lister(Squashfs3DetectionResult result)
    {
        _image = result.Image!;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        using var stream = new MemoryStream(_image, writable: false);
        using var fs = new Squashfs3FileSystem(stream);
        output.WriteLine($"Squashfs3 ({source.Name}, {fs.Root.GetFiles().Length + fs.Root.GetDirectories().Length} top entries):");
        Walk(fs, fs.Root, "", output);
    }

    private static void Walk(Squashfs3FileSystem fs, DiscDirectoryInfo dir, string prefix, TextWriter output)
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

public class Squashfs3Extractor : IFormatExtractor
{
    private static readonly ILog log = LogManager.GetLogger(nameof(Squashfs3Extractor));

    private readonly byte[] _image;

    public Squashfs3Extractor(Squashfs3DetectionResult result)
    {
        _image = result.Image!;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        using var stream = new MemoryStream(_image, writable: false);
        using var fs = new Squashfs3FileSystem(stream);
        foreach (var (path, content) in Walk(fs, fs.Root, ""))
        {
            // flat output: hierarchy stays visible via --list/--tree and mount
            var flat = path.Replace('/', '_');
            yield return ArtifactBuilder.Flat(source, flat, content);
        }
    }

    private static IEnumerable<(string Path, byte[] Content)> Walk(
        Squashfs3FileSystem fs, DiscDirectoryInfo dir, string prefix)
    {
        foreach (var d in dir.GetDirectories())
        {
            foreach (var sub in Walk(fs, d, prefix + d.Name + "/"))
            {
                yield return sub;
            }
        }
        foreach (var f in dir.GetFiles())
        {
            byte[] content;
            try
            {
                using var s = f.OpenRead();
                content = new byte[s.Length];
                int off = 0;
                while (off < content.Length)
                {
                    int n = s.Read(content, off, content.Length - off);
                    if (n <= 0) throw new InvalidDataException("short squashfs file read");
                    off += n;
                }
            }
            catch (Exception ex)
            {
                // hostile entries (e.g. dangling symlinks): keep siblings
                log.Warn($"skipping squashfs entry '{f.FullName}'", ex);
                continue;
            }
            yield return (prefix + f.Name, content);
        }
    }
}
