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

namespace Yafex.Fuse;

/// <summary>
/// A file node that can hand out a sequential stream over its content, so
/// consumers avoid per-chunk reopen costs (e.g. inflate restarts).
/// </summary>
public interface IVfsStreamable
{
    Stream OpenRead();
}

/// <summary>One node met by <see cref="VfsWalk.Walk"/>.</summary>
/// <param name="Path">`/`-separated path relative to the walk root.</param>
/// <param name="Depth">0 for the root's direct children.</param>
/// <param name="Error">Why a directory failed to expand (it then has no children).</param>
public sealed record VfsEntry(string Path, IVfsNode Node, int Depth, Exception? Error = null)
{
    public bool IsDirectory => Node.Type == VfsNodeType.Directory;
    public long Size => IsDirectory ? 0 : Node.Size;
}

/// <summary>
/// The single traversal over a mount tree: listing, tree printing, export and
/// the GUI all walk the same nodes in the same (ordinal, depth-first) order.
/// </summary>
public static class VfsWalk
{
    /// <summary>
    /// Depth-first pre-order walk of <paramref name="root"/>'s descendants
    /// (root itself excluded). Lazy directories expand as they are reached;
    /// a failed expansion is reported on its entry and the walk continues.
    /// </summary>
    public static IEnumerable<VfsEntry> Walk(IVfsNode root)
    {
        return WalkChildren(root, "", 0);
    }

    private static IEnumerable<VfsEntry> WalkChildren(IVfsNode dir, string prefix, int depth)
    {
        foreach (var child in Children(dir))
        {
            var path = prefix + child.Name;
            if (child.Type != VfsNodeType.Directory)
            {
                yield return new VfsEntry(path, child, depth);
                continue;
            }
            var error = Load(child);
            yield return new VfsEntry(path, child, depth, error);
            foreach (var sub in WalkChildren(child, path + "/", depth + 1))
            {
                yield return sub;
            }
        }
    }

    /// <summary>Loaded children of <paramref name="dir"/>, ordinal by name.</summary>
    public static IReadOnlyList<IVfsNode> Children(IVfsNode dir)
    {
        Load(dir);
        return dir.Tree.Values
            .Select(l => l.Node)
            .OrderBy(n => n.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static Exception? Load(IVfsNode dir)
    {
        try
        {
            dir.EnsureLoaded();
        }
        catch (Exception ex)
        {
            return ex;
        }
        return (dir as LazyVfsDirectory)?.LoadError;
    }

    /// <summary>
    /// Sequential stream over a file node: the node's own stream when it has
    /// one, else chunked <see cref="IVfsFile.Read"/> calls.
    /// </summary>
    public static Stream OpenRead(IVfsNode node)
    {
        return node switch
        {
            IVfsStreamable s => s.OpenRead(),
            IVfsFile f => new VfsFileStream(f, node.Size),
            _ => throw new ArgumentException($"'{node.Name}' is not a file", nameof(node)),
        };
    }

    /// <summary>Read-only seekable adapter over <see cref="IVfsFile.Read"/>.</summary>
    private sealed class VfsFileStream : Stream
    {
        private readonly IVfsFile _file;
        private readonly long _length;
        private long _position;

        public VfsFileStream(IVfsFile file, long length)
        {
            _file = file;
            _length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _length) return 0;
            var chunk = _file.Read(_position, Math.Min(count, _length - _position));
            if (chunk == null || chunk.Length == 0) return 0;
            chunk.CopyTo(buffer, offset);
            _position += chunk.Length;
            return chunk.Length;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            if (target < 0) throw new IOException("seek before start");
            _position = target;
            return target;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>Uniform text renderings of a walk, shared by CLI and tests.</summary>
public static class VfsListing
{
    /// <summary>
    /// One line per entry: `d|f  size  path`, sizes right-aligned; failed
    /// expansions get a trailing `!` note.
    /// </summary>
    public static void WriteFlat(IVfsNode root, TextWriter output)
    {
        foreach (var e in VfsWalk.Walk(root))
        {
            var kind = e.IsDirectory ? 'd' : 'f';
            var line = $"{kind} {e.Size,12} {e.Path}{(e.IsDirectory ? "/" : "")}";
            output.WriteLine(e.Error == null ? line : $"{line}  ! {e.Error.Message}");
        }
    }

    /// <summary>Indented tree, same order and error notes as <see cref="WriteFlat"/>.</summary>
    public static void WriteTree(IVfsNode root, TextWriter output)
    {
        foreach (var e in VfsWalk.Walk(root))
        {
            var indent = new string(' ', e.Depth * 2);
            var line = e.IsDirectory
                ? $"{indent}{e.Node.Name}/"
                : $"{indent}{e.Node.Name} ({e.Size} bytes)";
            output.WriteLine(e.Error == null ? line : $"{line}  ! {e.Error.Message}");
        }
    }
}

/// <summary>Writes a mount tree to disk: same layout as the mount.</summary>
public static class VfsExport
{
    private static readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof(VfsExport));

    /// <summary>
    /// Exports every descendant of <paramref name="root"/> under
    /// <paramref name="destDir"/>. Unreadable files are logged and skipped
    /// (siblings continue). Returns the number of files written.
    /// </summary>
    public static int Export(IVfsNode root, string destDir, Action<VfsEntry>? progress = null)
    {
        var baseDir = Path.GetFullPath(destDir);
        Directory.CreateDirectory(baseDir);
        int written = 0;
        foreach (var e in VfsWalk.Walk(root))
        {
            var target = SafeTarget(baseDir, e.Path);
            if (target == null)
            {
                log.Warn($"skipping '{e.Path}': escapes the export directory");
                continue;
            }
            progress?.Invoke(e);
            if (e.IsDirectory)
            {
                Directory.CreateDirectory(target);
                continue;
            }
            try
            {
                ExportFile(e.Node, target);
                written++;
            }
            catch (Exception ex)
            {
                log.Warn($"skipping unreadable '{e.Path}'", ex);
            }
        }
        return written;
    }

    /// <summary>Writes one file node's content to <paramref name="path"/>.</summary>
    public static void ExportFile(IVfsNode file, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var input = VfsWalk.OpenRead(file);
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        input.CopyTo(output, 1 << 20);
    }

    /// <summary>
    /// Host path for a walk path, or null when a hostile name (`..`, rooted,
    /// separators smuggled in a component) would leave <paramref name="baseDir"/>.
    /// </summary>
    public static string? SafeTarget(string baseDir, string relPath)
    {
        var parts = relPath.Split('/');
        foreach (var p in parts)
        {
            if (p.Length == 0 || p == "." || p == ".." || p.IndexOfAny(['\\', '\0']) >= 0)
            {
                return null;
            }
        }
        var full = Path.GetFullPath(Path.Combine([baseDir, .. parts]));
        var root = baseDir.EndsWith(Path.DirectorySeparatorChar) ? baseDir : baseDir + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.Ordinal) ? full : null;
    }
}
