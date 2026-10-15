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

using Yafex.FileSystems.Ufs;

using Smx.SharpIO;

namespace Yafex.FileFormats.Ufs;

public record UfsDetectionResult(int Confidence, byte[]? Image) : DetectionResult(Confidence);

public class UfsDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 9568) return DetectionResult.Fail;
            // zero-copy validation over the resident span
            using (var head = new SpanStream(data))
            {
                if (!UfsFileSystem.Detect(head))
                {
                    return DetectionResult.Fail;
                }
            }
            var bytes = data.ToArray();
            using var stream = new MemoryStream(bytes, writable: false);
            using var fs = new UfsFileSystem(stream);
            if (fs.Root == null) return DetectionResult.Fail;
            return new UfsDetectionResult(90, bytes);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class UfsExtractor : IFormatExtractor
{
    private readonly byte[] _image;

    public UfsExtractor(UfsDetectionResult result)
    {
        _image = result.Image!;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        using var stream = new MemoryStream(_image, writable: false);
        var ctx = UfsContext.Open(stream);
        foreach (var (path, content) in Walk(ctx, 2, ""))
        {
            // flat output like the other filesystem extractors
            var flat = path.Replace('/', '_');
            yield return ArtifactBuilder.Flat(source, flat, content);
        }
    }

    private static IEnumerable<(string Path, byte[] Content)> Walk(UfsContext ctx, uint ino, string prefix)
    {
        foreach (var e in ctx.ReadDirectory(ino).OrderBy(x => x.Name))
        {
            if (e.Name == "." || e.Name == "..")
            {
                continue;
            }
            var inode = ctx.GetInode(e.Ino);
            var path = prefix + e.Name;
            if (inode.IsDirectory)
            {
                foreach (var sub in Walk(ctx, e.Ino, path + "/"))
                {
                    yield return sub;
                }
            }
            else if (inode.IsSymlink)
            {
                var raw = ctx.ReadSymlink(e.Ino);
                int len = raw.Length;
                while (len > 0 && raw[len - 1] == 0) len--;
                yield return (path, System.Text.Encoding.UTF8.GetBytes(
                    System.Text.Encoding.UTF8.GetString(raw, 0, len)));
            }
            else
            {
                yield return (path, ctx.ReadFile(e.Ino));
            }
        }
    }
}
