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

using Yafex.FileSystems.Jffs2;

namespace Yafex.FileFormats.Jffs2;

public record Jffs2DetectionResult(int Confidence, byte[]? Image) : DetectionResult(Confidence);

public class Jffs2Detector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 12) return DetectionResult.Fail;
            var bytes = data.ToArray();
            // cheap gate first (magic at offset 0, like the reference tool);
            // the full open below is the strong validation
            using (var head = new MemoryStream(bytes, writable: false))
            {
                if (!Jffs2FileSystem.Detect(head))
                {
                    return DetectionResult.Fail;
                }
            }
            // full open while validating (images are small config parts)
            using var stream = new MemoryStream(bytes, writable: false);
            using var fs = new Jffs2FileSystem(stream);
            var root = fs.Root;
            if (root == null) return DetectionResult.Fail;
            return new Jffs2DetectionResult(90, bytes);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class Jffs2Lister : IFormatLister
{
    private readonly byte[] _image;

    public Jffs2Lister(Jffs2DetectionResult result)
    {
        _image = result.Image!;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        using var stream = new MemoryStream(_image, writable: false);
        var ctx = Jffs2Context.Open(stream);
        var top = ctx.ReadDirectory(1);
        output.WriteLine($"Jffs2 ({source.Name}, {top.Count} top entries):");
        Walk(ctx, 1, "", output);
    }

    private static void Walk(Jffs2Context ctx, uint ino, string prefix, TextWriter output)
    {
        foreach (var e in ctx.ReadDirectory(ino).OrderBy(x => x.Name))
        {
            var inode = ctx.GetInode(e.Ino);
            bool dir = e.Dtype == 4 || (inode.Mode & 0xF000) == 0x4000;
            bool link = !dir && (e.Dtype == 10 || (inode.Mode & 0xF000) == 0xA000);
            var kind = dir ? "d" : link ? "l" : "f";
            output.WriteLine($"  {kind} {prefix}{e.Name} size={inode.Size}");
            if (dir)
            {
                Walk(ctx, e.Ino, prefix + e.Name + "/", output);
            }
        }
    }
}

public class Jffs2Extractor : IFormatExtractor
{
    private readonly byte[] _image;

    public Jffs2Extractor(Jffs2DetectionResult result)
    {
        _image = result.Image!;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        using var stream = new MemoryStream(_image, writable: false);
        var ctx = Jffs2Context.Open(stream);
        foreach (var (path, content) in Walk(ctx, 1, ""))
        {
            // flat output like the other filesystem extractors
            var flat = path.Replace('/', '_');
            yield return ArtifactBuilder.Flat(source, flat, content);
        }
    }

    private static IEnumerable<(string Path, byte[] Content)> Walk(Jffs2Context ctx, uint ino, string prefix)
    {
        foreach (var e in ctx.ReadDirectory(ino).OrderBy(x => x.Name))
        {
            var inode = ctx.GetInode(e.Ino);
            bool dir = e.Dtype == 4 || (inode.Mode & 0xF000) == 0x4000;
            bool link = !dir && (e.Dtype == 10 || (inode.Mode & 0xF000) == 0xA000);
            var path = prefix + e.Name;
            if (dir)
            {
                foreach (var sub in Walk(ctx, e.Ino, path + "/"))
                {
                    yield return sub;
                }
            }
            else if (link)
            {
                var raw = ctx.ReadFile(e.Ino);
                int len = raw.Length;
                while (len > 0 && raw[len - 1] == 0) len--;
                yield return (path, System.Text.Encoding.ASCII.GetBytes(
                    System.Text.Encoding.ASCII.GetString(raw, 0, len)));
            }
            else
            {
                yield return (path, ctx.ReadFile(e.Ino));
            }
        }
    }
}
