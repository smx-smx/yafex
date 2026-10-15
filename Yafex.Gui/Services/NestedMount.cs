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

using Smx.SharpIO.Memory.Buffers;

using Yafex.Fuse;
using Yafex.Support;

namespace Yafex.Gui.Services;

/// <summary>A browsable tree found inside a file, plus what keeps it alive.</summary>
/// <param name="Root">Directory node to browse.</param>
/// <param name="Label">What was recognized (expander or format name).</param>
/// <param name="Owner">Buffers/readers to release when the tree is dropped.</param>
public sealed record NestedTree(IVfsNode Root, string Label, IDisposable Owner);

/// <summary>
/// Opens files as containers: a top-level image from disk, or any file node
/// inside an already browsed tree ("explore as container").
/// </summary>
public static class NestedMount
{
    /// <summary>Members above this are spooled to a temp file instead of RAM for detection.</summary>
    private const long BufferLimit = 256L << 20;

    /// <summary>
    /// Browsable tree for a file node: the content-expander registry first (cheap
    /// magic sniff, stays lazy and streaming), else full format detection over a
    /// buffered copy. Null when nothing recognizes the content.
    /// </summary>
    public static NestedTree? TryMount(IVfsNode file, FormatFinder finder)
    {
        var image = new VfsNodeVirtualFile(file);
        var keepAlive = new List<object> { image };
        var expander = ContentExpanders.FindMatch(image);
        if (expander != null)
        {
            try
            {
                var dir = expander.Expand(image, keepAlive);
                return new NestedTree(dir, expander.Name, new Owned(keepAlive));
            }
            catch (Exception)
            {
                // fall through to format detection
            }
        }

        var (source, owner) = Buffer(image);
        try
        {
            var hits = finder.ProbeAll(source);
            if (hits.Count == 0)
            {
                owner.Dispose();
                return null;
            }
            var best = hits[0];
            var vfs = VfsMount.CreateLazy(best.Addon, best.Result, source, image.Name);
            if (vfs == null)
            {
                owner.Dispose();
                return null;
            }
            return new NestedTree(TopNode(vfs.Root), FormatName(best.Format), owner);
        }
        catch (Exception)
        {
            owner.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens an image from disk with one probe pass: the best format's lazy
    /// tree, else the expander registry over the whole file (FAT, ext, GPT, ...).
    /// Returns the tree (null when nothing applies) and every format that
    /// matched, best first.
    /// </summary>
    public static (NestedTree? Tree, IReadOnlyList<string> Matches) OpenFile(string path, FormatFinder finder)
    {
        var input = new MFile(path);
        try
        {
            var hits = finder.ProbeAll(input);
            var matches = hits.Select(h => $"{FormatName(h.Format)} ({h.Result.Confidence})").ToList();
            var best = hits.Count > 0 ? hits[0] : default;
            var vfs = VfsMount.CreateLazy(best.Addon, best.Result, input, Path.GetFileName(path));
            if (vfs == null)
            {
                input.Dispose();
                return (null, matches);
            }
            var label = best.Addon != null ? FormatName(best.Format) : "expanded";
            return (new NestedTree(TopNode(vfs.Root), label, new Owned(new List<object> { input })), matches);
        }
        catch (Exception)
        {
            input.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The directory a mount presents for its input: lazy mounts wrap the input
    /// in one directory named after it, which is what users expect to see.
    /// </summary>
    public static IVfsNode TopNode(IVfsNode vfsRoot)
    {
        vfsRoot.EnsureLoaded();
        var children = vfsRoot.Tree.Values.Select(l => l.Node).ToList();
        return children.Count == 1 && children[0].Type == VfsNodeType.Directory ? children[0] : vfsRoot;
    }

    public static string FormatName(FileFormat format) => Enum.GetName(format)?.ToLowerInvariant() ?? format.ToString();

    private static (IDataSource Source, IDisposable Owner) Buffer(IVirtualFile image)
    {
        if (image.Length <= BufferLimit)
        {
            var mem = new MemoryDataSource(new Memory64<byte>(image.ReadAllBytes()))
            {
                Name = image.Name,
                Flags = DataSourceFlags.Input,
            };
            return (mem, new Owned(new List<object>()));
        }
        var temp = Path.Combine(Path.GetTempPath(), $"yafex-{Guid.NewGuid():N}-{PathNames.Flatten(image.Name, "member")}");
        using (var input = image.OpenRead())
        using (var output = File.Create(temp))
        {
            input.CopyTo(output, 1 << 20);
        }
        var mapped = new MFile(temp);
        return (mapped, new Owned(new List<object> { mapped }, temp));
    }

    /// <summary>Disposes kept-alive objects and deletes a spool file, once.</summary>
    private sealed class Owned : IDisposable
    {
        private readonly List<object> _items;
        private readonly string? _tempFile;
        private bool _disposed;

        public Owned(List<object> items, string? tempFile = null)
        {
            _items = items;
            _tempFile = tempFile;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var item in _items.OfType<IDisposable>())
            {
                try { item.Dispose(); } catch (Exception) { }
            }
            if (_tempFile != null)
            {
                try { File.Delete(_tempFile); } catch (Exception) { }
            }
        }
    }
}
