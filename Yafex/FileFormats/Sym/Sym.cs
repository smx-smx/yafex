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
using System.Text;

namespace Yafex.FileFormats.Sym;

/// <summary>
/// LG .sym debug symbol tables (e.g. RELEASE.sym): header, {addr, end,
/// name-offset} entries, optional hash/dwarf sections, name blob.
/// </summary>
public static class Sym
{
    public const uint Magic = 0xB12791EE;
    public const int HeaderSize = 20;
    public const int EntrySize = 12;

    public sealed class Symbol
    {
        public uint Addr;
        public uint End;
        public string Name = "";
    }

    private static uint ReadU32LE(byte[] buf, int off)
    {
        return (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));
    }

    // Cheap head sniff shared with the mount expander.
    public static bool HasMagic(byte[] prefix)
    {
        return prefix.Length >= 4 && ReadU32LE(prefix, 0) == Magic;
    }

    public static bool TryParse(byte[] image, out List<Symbol> symbols)
    {
        symbols = new List<Symbol>();
        try
        {
            if (image.Length < HeaderSize || !HasMagic(image))
            {
                return false;
            }
            uint size = ReadU32LE(image, 8);
            uint count = ReadU32LE(image, 12);
            uint tail = ReadU32LE(image, 16);
            if (size + HeaderSize != image.Length || tail + EntrySize * count != size)
            {
                return false;
            }
            int pos = HeaderSize + (int)(EntrySize * count);
            // optional hash section (present as 2, absent as 0)
            uint hasHash = ReadU32LE(image, pos);
            pos += 4;
            if (hasHash == 2)
            {
                pos += (int)((count + 1) & ~1u) * 4;
            }
            else if (hasHash != 0)
            {
                return false;
            }
            // optional dwarf section (names follow it, else start here)
            int namesAt = pos;
            uint hasDwarf = ReadU32LE(image, pos);
            pos += 4;
            if (hasDwarf == 1)
            {
                uint nDwarf = ReadU32LE(image, pos);
                pos += 4;
                uint dwarfSize = ReadU32LE(image, pos);
                pos += 4;
                pos += (int)nDwarf * 8 + (int)dwarfSize;
                namesAt = pos;
            }
            else if (hasDwarf != 0)
            {
                return false;
            }
            if (namesAt > image.Length)
            {
                return false;
            }
            for (uint i = 0; i < count; i++)
            {
                int e = HeaderSize + (int)(i * EntrySize);
                uint addr = ReadU32LE(image, e);
                uint end = ReadU32LE(image, e + 4);
                uint nameOff = ReadU32LE(image, e + 8);
                int start = namesAt + (int)nameOff;
                if (start < namesAt || start >= image.Length)
                {
                    return false;
                }
                int len = 0;
                while (start + len < image.Length && image[start + len] != 0)
                {
                    len++;
                }
                symbols.Add(new Symbol
                {
                    Addr = addr,
                    End = end,
                    Name = Encoding.ASCII.GetString(image, start, len)
                });
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Escape(string name)
    {
        return name.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>IDA script (reference-compatible output).</summary>
    public static string ToIdc(List<Symbol> symbols)
    {
        var sb = new StringBuilder();
        sb.Append("#include <idc.idc>\n\n");
        sb.Append("static main() {\n");
        foreach (var s in symbols)
        {
            string name = Escape(s.Name);
            sb.Append($"MakeNameEx( 0x{s.Addr:x}, \"{name}\", SN_NOWARN | SN_CHECK);\n");
            sb.Append($"if(SegName(0x{s.Addr:x})==\".text\") {{\n");
            sb.Append($"   MakeCode(0x{s.Addr:x});\n");
            sb.Append($"   MakeFunction(0x{s.Addr:x}, 0x{s.End:x});\n");
            sb.Append("};\n");
        }
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>Ghidra script: same symbols via createLabel/createFunction.</summary>
    public static string ToGhidra(List<Symbol> symbols)
    {
        var sb = new StringBuilder();
        sb.Append("# Ghidra script: apply LG .sym symbol names (Script Manager).\n");
        sb.Append("# @category LG\n\n");
        sb.Append("syms = [\n");
        foreach (var s in symbols)
        {
            sb.Append($"    (0x{s.Addr:x}, 0x{s.End:x}, \"{Escape(s.Name)}\"),\n");
        }
        sb.Append("]\n\n");
        sb.Append("for addr, end, name in syms:\n");
        sb.Append("    try:\n");
        sb.Append("        a = toAddr(addr)\n");
        sb.Append("        createLabel(a, name, True)\n");
        sb.Append("        disassemble(a)\n");
        sb.Append("        createFunction(a, name)\n");
        sb.Append("    except Exception as e:\n");
        sb.Append("        print(\"sym %s @ %x: %s\" % (name, addr, e))\n");
        return sb.ToString();
    }
}
