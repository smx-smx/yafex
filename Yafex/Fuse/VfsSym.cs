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

using Yafex.FileFormats.Sym;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for LG .sym tables: exposes the generated IDA + Ghidra
/// scripts as files (tables have no browsable content of their own).
/// </summary>
public sealed class SymExpander : IContentExpander
{
    public string Name => "sym";

    public bool Matches(IVirtualFile file)
    {
        try
        {
            using var probe = file.OpenRead();
            var magic = new byte[4];
            if (probe.Read(magic, 0, 4) != 4) return false;
            return Sym.HasMagic(magic);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            var stem = file.Name.EndsWith(".sym", StringComparison.OrdinalIgnoreCase)
                ? file.Name[..^4]
                : file.Name;
            using var s = file.OpenRead();
            if (s.Length > int.MaxValue)
                throw new InvalidDataException("SYM too large");
            var buf = new byte[s.Length];
            int off = 0;
            while (off < buf.Length)
            {
                int n = s.Read(buf, off, buf.Length - off);
                if (n <= 0) throw new InvalidDataException("truncated SYM");
                off += n;
            }
            if (!Sym.TryParse(buf, out var symbols))
            {
                throw new InvalidDataException("not a SYM file");
            }
            var idc = new TextVirtualFile(stem + ".idc", Sym.ToIdc(symbols));
            var py = new TextVirtualFile(stem + "_ghidra.py", Sym.ToGhidra(symbols));
            keepAlive.Add(idc);
            keepAlive.Add(py);
            return new IVfsNode[]
            {
                new PakVfsFile(idc, Helpers.OctalLiteral(0444)),
                new PakVfsFile(py, Helpers.OctalLiteral(0444)),
            };
        });
    }
}
