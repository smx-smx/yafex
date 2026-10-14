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

using log4net;

using Yafex.FileSystems.Ubifs;

namespace Yafex.FileFormats.Ubifs;

public record UbifsDetectionResult(int Confidence, byte[]? Image) : DetectionResult(Confidence);

public class UbifsDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 32) return DetectionResult.Fail;
            var bytes = data.ToArray();
            using (var head = new MemoryStream(bytes, writable: false))
            {
                if (!UbifsFileSystem.Detect(head))
                {
                    return DetectionResult.Fail;
                }
            }
            using var stream = new MemoryStream(bytes, writable: false);
            using var fs = new UbifsFileSystem(stream);
            if (fs.Root == null) return DetectionResult.Fail;
            return new UbifsDetectionResult(90, bytes);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class UbifsLister : IFormatLister
{
    private readonly byte[] _image;

    public UbifsLister(UbifsDetectionResult result)
    {
        _image = result.Image!;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        using var stream = new MemoryStream(_image, writable: false);
        var ctx = UbifsContext.Open(stream);
        var top = ctx.ReadDirectory(1);
        output.WriteLine($"Ubifs ({source.Name}, {top.Count} top entries):");
        Walk(ctx, 1, "", output);
    }

    private static void Walk(UbifsContext ctx, uint ino, string prefix, TextWriter output)
    {
        foreach (var e in ctx.ReadDirectory(ino).OrderBy(x => x.Name))
        {
            var inode = ctx.GetInode(e.Ino);
            bool dir = (inode.Mode & 0xF000) == 0x4000;
            bool link = !dir && (inode.Mode & 0xF000) == 0xA000;
            var kind = dir ? "d" : link ? "l" : "f";
            long size = link ? ctx.ReadSymlink(e.Ino).Length : (long)inode.Size;
            output.WriteLine($"  {kind} {prefix}{e.Name} size={size}");
            if (dir)
            {
                Walk(ctx, e.Ino, prefix + e.Name + "/", output);
            }
        }
    }
}

public class UbifsExtractor : IFormatExtractor
{
    private static readonly ILog log = LogManager.GetLogger(nameof(UbifsExtractor));

    private readonly byte[] _image;

    public UbifsExtractor(UbifsDetectionResult result)
    {
        _image = result.Image!;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        using var stream = new MemoryStream(_image, writable: false);
        var ctx = UbifsContext.Open(stream);
        foreach (var (path, content) in Walk(ctx, 1, ""))
        {
            // flat output like the other filesystem extractors
            var flat = path.Replace('/', '_');
            yield return ArtifactBuilder.Flat(source, flat, content);
        }
    }

    private static IEnumerable<(string Path, byte[] Content)> Walk(UbifsContext ctx, uint ino, string prefix)
    {
        foreach (var e in ctx.ReadDirectory(ino).OrderBy(x => x.Name))
        {
            var inode = ctx.GetInode(e.Ino);
            bool dir = (inode.Mode & 0xF000) == 0x4000;
            bool link = !dir && (inode.Mode & 0xF000) == 0xA000;
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
                byte[] target;
                try
                {
                    target = ctx.ReadSymlink(e.Ino);
                }
                catch (Exception ex)
                {
                    // hostile entries: keep siblings
                    log.Warn($"skipping ubifs entry '{path}'", ex);
                    continue;
                }
                yield return (path, target);
            }
            else
            {
                byte[] content;
                try
                {
                    content = ctx.ReadFile(e.Ino);
                }
                catch (Exception ex)
                {
                    // hostile entries: keep siblings
                    log.Warn($"skipping ubifs entry '{path}'", ex);
                    continue;
                }
                yield return (path, content);
            }
        }
    }
}
