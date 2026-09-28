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

using Yafex.FileFormats.IntelHex;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for micom Intel HEX images: exposes the decoded flat
/// binary (leaves, no chaining).
/// </summary>
public sealed class IntelHexExpander : IContentExpander
{
    public string Name => "intelhex";

    public bool Matches(IVirtualFile file)
    {
        return ExpanderSniff.Head(file, 32, head =>
        {
            // header line DDDD:hex, then a ':' record
            var text = Encoding.ASCII.GetString(head, 0, head.Length);
            int nl = text.IndexOf('\n');
            if (nl < 0) return false;
            string first = text[..nl].Trim();
            if (first.Length < 6 || first[4] != ':') return false;
            for (int i = 0; i < 4; i++)
            {
                if (!char.IsDigit(first[i])) return false;
            }
            return text.IndexOf("\n:", StringComparison.Ordinal) >= 0;
        });
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            using var s = file.OpenRead();
            if (s.Length > int.MaxValue)
                throw new InvalidDataException("HEX too large");
            var buf = new byte[s.Length];
            int off = 0;
            while (off < buf.Length)
            {
                int n = s.Read(buf, off, buf.Length - off);
                if (n <= 0) throw new InvalidDataException("truncated HEX");
                off += n;
            }
            if (!IntelHex.TryParse(buf, out var image))
            {
                throw new InvalidDataException("not Intel HEX");
            }
            var flat = IntelHex.ToImage(image, out _);
            var stem = file.Name;
            foreach (var ext in new[] { ".pak", ".hex", ".lr" })
            {
                if (stem.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    stem = stem[..^ext.Length];
                    break;
                }
            }
            var node = new IntelHexVirtualFile(stem + ".bin", flat);
            keepAlive.Add(node);
            return new IVfsNode[] { new PakVfsFile(node, Helpers.OctalLiteral(0444)) };
        });
    }
}

internal sealed class IntelHexVirtualFile : IVirtualFile
{
    private readonly byte[] _data;

    public IntelHexVirtualFile(string name, byte[] data)
    {
        Name = name;
        _data = data;
    }

    public string Name { get; }
    public long Length => _data.Length;
    public Stream OpenRead() => new MemoryStream(_data, writable: false);
}
