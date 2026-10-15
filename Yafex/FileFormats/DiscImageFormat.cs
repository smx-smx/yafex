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

using Yafex.FileSystems.Base;
using Yafex.Fuse;

namespace Yafex.FileFormats;

/// <summary>
/// Detection result for a filesystem image buffered in memory: the image and
/// where the filesystem starts inside it (non-zero for embedded images).
/// </summary>
public abstract record DiscImageDetectionResult(int Confidence, byte[]? Image, long Offset = 0)
    : DetectionResult(Confidence)
{
    /// <summary>Read-only stream over the filesystem bytes, starting at <see cref="Offset"/>.</summary>
    public Stream OpenImage()
    {
        var image = Image ?? throw new InvalidOperationException("detection result carries no image");
        return new MemoryStream(image, (int)Offset, image.Length - (int)Offset, writable: false);
    }
}

/// <summary>
/// Base addon for read-only filesystem images backed by a <see cref="DiscFileSystem"/>
/// driver. A format supplies its detector and how to open the driver; listing
/// (--list), flat extraction and the lazy mount tree (--mount/--tree/GUI) are shared.
/// </summary>
public abstract class DiscImageAddon<TResult> : FormatAddon<TResult>
    where TResult : DiscImageDetectionResult
{
    /// <summary>Opens the driver over <paramref name="image"/> (positioned at the filesystem start).</summary>
    public abstract DiscFileSystem OpenFileSystem(Stream image);

    /// <summary>Heading printed by the lister.</summary>
    protected virtual string DisplayName => FileFormat.ToString();

    protected override bool IsUsable(TResult result) => result.Image != null;

    protected override IFormatExtractor CreateExtractor(TResult result)
    {
        return new DiscImageExtractor(OpenFor(result));
    }

    protected override IFormatLister? CreateLister(TResult result)
    {
        return new DiscImageLister(DisplayName, OpenFor(result));
    }

    // Lifetime: the factory closure keeps the image and driver alive for as
    // long as the mount node is reachable.
    public override YafexVfs? CreateVfs(IDataSource source, DetectionResult result)
    {
        if (result is not TResult { Image: not null } typed) return null;
        var open = OpenFor(typed);
        var vfs = new YafexVfs();
        vfs.Root.AddNode(new LazyVfsDirectory(source.Name, Helpers.OctalLiteral(0755),
            () => DiscTree.Children(open())));
        return vfs;
    }

    private Func<DiscFileSystem> OpenFor(TResult result) => () => OpenFileSystem(result.OpenImage());
}

/// <summary>Recursive `d`/`f` listing of a <see cref="DiscFileSystem"/>.</summary>
public sealed class DiscImageLister : IFormatLister
{
    private readonly string _displayName;
    private readonly Func<DiscFileSystem> _open;

    public DiscImageLister(string displayName, Func<DiscFileSystem> open)
    {
        _displayName = displayName;
        _open = open;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        using var fs = _open();
        var top = fs.Root.GetFiles().Length + fs.Root.GetDirectories().Length;
        output.WriteLine($"{_displayName} ({source.Name}, {top} top entries):");
        Walk(fs.Root, "", output);
    }

    private static void Walk(DiscDirectoryInfo dir, string prefix, TextWriter output)
    {
        foreach (var d in dir.GetDirectories())
        {
            output.WriteLine($"  d {prefix}{d.Name}");
            Walk(d, prefix + d.Name + "/", output);
        }
        foreach (var f in dir.GetFiles())
        {
            output.WriteLine($"  f {prefix}{f.Name} size={f.Length}");
        }
    }
}

/// <summary>Flat extraction of a <see cref="DiscFileSystem"/> (`a/b` becomes `a_b`).</summary>
public sealed class DiscImageExtractor : IFormatExtractor
{
    private readonly Func<DiscFileSystem> _open;

    public DiscImageExtractor(Func<DiscFileSystem> open)
    {
        _open = open;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        using var fs = _open();
        foreach (var (path, content) in DiscTree.Flatten(fs))
        {
            // flat output: hierarchy stays visible via --list/--tree and mount
            yield return ArtifactBuilder.Flat(source, path.Replace('/', '_'), content);
        }
    }
}
