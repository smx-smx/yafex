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

using Yafex.FileFormats.Lzo;

namespace Yafex.Fuse;

/// <summary>
/// Decompressed view of an LG-LZO stream. The payload is decoded once on first
/// access and memoized (typical payloads are a few tens of MB); every open
/// then serves plain random-access reads from memory.
/// </summary>
public sealed class LgLzoVirtualFile : IVirtualFile
{
    private readonly IVirtualFile _source;
    private byte[]? _decoded;
    private readonly object _gate = new();

    public LgLzoVirtualFile(string name, IVirtualFile source)
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
            _decoded = LgLzo.DecompressAll(buf);
        }
    }
}

/// <summary>
/// Mount expander for LG-LZO streams (e.g. lgap.pak): exposes the decompressed
/// payload, chaining into the nested-format expander when the payload itself
/// is a filesystem (cramfs), so lgap.pak.d/ lists the filesystem directly.
/// </summary>
public sealed class LgLzoExpander : IContentExpander
{
    public string Name => "lg-lzo";

    public bool Matches(IVirtualFile file)
    {
        return ExpanderSniff.Head(file, LgLzo.Magic.Length, magic => LgLzo.HasMagic(magic));
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            var stem = file.Name.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)
                ? file.Name[..^4]
                : file.Name;
            var unlzo = new LgLzoVirtualFile(stem + ".unlzo", file);
            keepAlive.Add(unlzo);
            var nodes = new List<IVfsNode> { new PakVfsFile(unlzo, Helpers.OctalLiteral(0444)) };
            // nested filesystem (cramfs): surface its tree next to the blob
            try
            {
                if (ContentExpanders.TryExpand(unlzo, keepAlive, out var inner) && inner != null)
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
                // keep just the decompressed blob
            }
            return nodes;
        });
    }
}
