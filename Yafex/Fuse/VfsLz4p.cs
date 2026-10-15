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

using Yafex.FileFormats.Lz4;

namespace Yafex.Fuse;

/// <summary>
/// Decoded view of an LG LZ4P archive, memoized like the LZO one (payloads
/// are a few MB to tens of MB).
/// </summary>
public sealed class Lz4pVirtualFile : IVirtualFile
{
    private readonly IVirtualFile _source;
    private byte[]? _decoded;
    private readonly object _gate = new();

    public Lz4pVirtualFile(string name, IVirtualFile source)
    {
        Name = name;
        _source = source;
    }

    public string Name { get; }

    public long Length
    {
        get
        {
            EnsureDecoded();
            return _decoded!.Length;
        }
    }

    public Stream OpenRead()
    {
        EnsureDecoded();
        return new MemoryStream(_decoded!, writable: false);
    }

    private void EnsureDecoded()
    {
        if (_decoded != null) return;
        lock (_gate)
        {
            if (_decoded != null) return;
            var buf = _source.ReadAllBytes();
            _decoded = Lz4p.Decode(buf);
        }
    }
}

/// <summary>
/// Mount expander for LG LZ4P archives (e.g. nc45 kern.pak): exposes the
/// decoded payload, chaining into the nested-format expander when the payload
/// itself is an image (uImage) or filesystem.
/// </summary>
public sealed class Lz4pExpander : IContentExpander
{
    public string Name => "lz4p";

    public bool Matches(IVirtualFile file)
    {
        return ExpanderSniff.Head(file, Lz4p.Magic.Length, Lz4p.HasMagic);
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            var decoded = new Lz4pVirtualFile(file.Name + ".unlz4", file);
            keepAlive.Add(decoded);
            var nodes = new List<IVfsNode> { new PakVfsFile(decoded, Helpers.OctalLiteral(0444)) };
            try
            {
                if (ContentExpanders.TryExpand(decoded, keepAlive, out var inner) && inner != null)
                {
                    inner.EnsureLoaded();
                    foreach (var entry in inner.Tree.Values.Select(l => l.Node))
                    {
                        nodes.Add(entry);
                    }
                }
            }
            catch (Exception)
            {
                // keep just the decoded blob
            }
            return nodes;
        });
    }
}
