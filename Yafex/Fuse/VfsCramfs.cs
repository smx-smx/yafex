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
using System.Text;

using Yafex.FileFormats.Cramfs;

namespace Yafex.Fuse;

/// <summary>
/// VFS file backed by one cramfs entry. Content is materialized once per node
/// (firmware files are small; the 20MB RELEASE-class blobs decode in ~1s and
/// then serve random access from memory). Symlinks have no VFS node type, so
/// they surface as tiny files holding the target path (same as extraction).
/// </summary>
public sealed class CramfsFileNode : VfsNode, IVfsFile
{
    private readonly CramfsReader _reader;
    private readonly CramfsInode _inode;
    private byte[]? _content;
    private readonly object _gate = new();

    public CramfsFileNode(CramfsReader reader, CramfsInode inode, int mode)
        : base(inode.Name, mode, (long)reader.FileSize(inode))
    {
        _reader = reader;
        _inode = inode;
    }

    public override VfsNodeType Type => VfsNodeType.File;

    private byte[] Content()
    {
        if (_content != null) return _content;
        lock (_gate)
        {
            if (_content != null) return _content;
            _content = _inode.IsSymlink
                ? Encoding.ASCII.GetBytes(_reader.ReadSymlink(_inode))
                : _reader.ReadFile(_inode);
            return _content;
        }
    }

    public byte[]? Read(long offset, long count)
    {
        var content = Content();
        if (offset > content.Length) return null;
        var len = (int)Math.Min(count, content.Length - offset);
        var buf = new byte[len];
        Array.Copy(content, offset, buf, 0, len);
        return buf;
    }

    public int Rename(string newName) => -(int)PosixError.ENOTSUP;
    public int Truncate(long length) => -(int)PosixError.ENOTSUP;
    public int Write(byte[] buffer, long offset, long count) => -(int)PosixError.ENOTSUP;
}

/// <summary>
/// Mount expander for cramfs images (including LG-flavored ones with
/// gid-extended sizes and XIP raw files). The image is materialized once per
/// mount; entries become a lazy directory tree.
/// </summary>
public sealed class CramfsExpander : IContentExpander
{
    public string Name => "cramfs";

    public bool Matches(IVirtualFile file)
    {
        return ExpanderSniff.Head(file, 4, magic => CramfsReader.HasMagic(magic, out _));
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            var image = ReadAll(file);
            if (!CramfsReader.HasMagic(image, out bool bigEndian))
            {
                throw new InvalidDataException("not a cramfs image");
            }
            var reader = new CramfsReader(image, bigEndian);
            keepAlive.Add(reader);
            keepAlive.Add(image);
            var tmp = new YafexDirectory(file.Name, Helpers.OctalLiteral(0755));
            foreach (var node in BuildDir(reader, reader.Root))
            {
                tmp.AddNode(node);
            }
            return tmp.Tree.Values.Select(l => l.Node);
        });
    }

    private static List<IVfsNode> BuildDir(CramfsReader reader, CramfsInode dir)
    {
        var nodes = new List<IVfsNode>();
        foreach (var e in reader.ReadDirectory(dir))
        {
            if (e.IsDirectory)
            {
                var sub = new YafexDirectory(e.Name, Helpers.OctalLiteral(0755));
                foreach (var child in BuildDir(reader, e))
                {
                    sub.AddNode(child);
                }
                nodes.Add(sub);
            }
            else if (e.IsRegular || e.IsSymlink)
            {
                nodes.Add(new CramfsFileNode(reader, e, Helpers.OctalLiteral(0444)));
            }
        }
        return nodes;
    }

    private static byte[] ReadAll(IVirtualFile file)
    {
        using var s = file.OpenRead();
        if (s.Length > int.MaxValue)
            throw new InvalidDataException("cramfs image too large");
        var buf = new byte[s.Length];
        int off = 0;
        while (off < buf.Length)
        {
            int n = s.Read(buf, off, buf.Length - off);
            if (n <= 0) throw new InvalidDataException("truncated cramfs image");
            off += n;
        }
        return buf;
    }
}
