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

public record CramfsDetectionResult(int Confidence, byte[]? Image) : DetectionResult(Confidence);

public class CramfsDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 76) return DetectionResult.Fail;
            var prefix = data.Slice(0, 32).ToArray();
            if (BitConverter.ToUInt32(prefix, 0) != CramfsReader.Magic) return DetectionResult.Fail;
            // full validation (signature, root): needs the whole image
            var bytes = data.ToArray();
            var reader = new CramfsReader(bytes);
            return new CramfsDetectionResult(90, bytes);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class CramfsLister : IFormatLister
{
    private readonly byte[] _image;

    public CramfsLister(CramfsDetectionResult result)
    {
        _image = result.Image!;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        var reader = new CramfsReader(_image);
        output.WriteLine($"Cramfs ({source.Name}, {reader.DeclaredSize} bytes):");
        Walk(reader, reader.Root, "", output);
    }

    private static void Walk(CramfsReader reader, CramfsInode dir, string prefix, TextWriter output)
    {
        foreach (var e in reader.ReadDirectory(dir))
        {
            var kind = e.IsDirectory ? "d" : e.IsSymlink ? "l" : "f";
            var extra = e.IsSymlink ? $" -> {reader.ReadSymlink(e)}"
                : e.IsRegular && e.IsRaw ? " (raw)" : "";
            output.WriteLine($"  {kind} {prefix}{e.Name} size={reader.FileSize(e)}{extra}");
            if (e.IsDirectory)
            {
                Walk(reader, e, prefix + e.Name + "/", output);
            }
        }
    }
}

public class CramfsExtractor : IFormatExtractor
{
    private readonly byte[] _image;

    public CramfsExtractor(CramfsDetectionResult result)
    {
        _image = result.Image!;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var reader = new CramfsReader(_image);
        foreach (var (path, content) in Walk(reader, reader.Root, ""))
        {
            // flat output: hierarchy stays visible via --list/--tree and mount.
            // (A subdir per image would collide with the image file itself,
            // e.g. "lgap.unlzo/" next to "lgap.unlzo".)
            var flat = path.Replace('/', '_');
            var artifact = new MemoryDataSource(content)
            {
                Name = flat
            };
            artifact.SetChildOf(source);
            artifact.AddMetadata(new Metadata.OutputFileName(flat));
            yield return artifact;
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
