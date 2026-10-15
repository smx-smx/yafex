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
using Yafex.Support;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Yafex.FileFormats.IntelHex;

/// <summary>
/// Micom firmware in Intel HEX form (mico/micom.pak outside the MPAK
/// wrapper): a `DDDD:hex` header line, `:LLAAAATT..CC` records (types
/// 00/01/02/03/04/05), DOS EOF marker trailer. Decodes to a flat image
/// (gaps filled with 0xFF, the erased-flash value).
/// </summary>
public static class IntelHex
{
    public sealed class Segment
    {
        public long Start;
        public byte[] Data = Array.Empty<byte>();
    }

    public sealed class Image
    {
        public string HeaderCode = "";
        public string HeaderPayload = "";
        public List<Segment> Segments = new List<Segment>();
        public long StartAddress;
        public bool HasStartAddress;
    }

    private static bool IsHex(char c)
    {
        return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
    }

    private static int HexNibble(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return c - 'a' + 10;
    }

    /// <summary>The printable-text rule <see cref="TryParse"/> applies, on a prefix.</summary>
    public static bool LooksLikeText(byte[] prefix)
    {
        foreach (byte b in prefix)
        {
            if (b != '\r' && b != '\n' && b != 0x1A && (b < 0x20 || b > 0x7E)) return false;
        }
        return prefix.Length > 0;
    }

    public static bool TryParse(byte[] blob, out Image image)
    {
        image = new Image();
        try
        {
            string text = Encoding.ASCII.GetString(blob);
            // reject binary masquerading as text early
            foreach (char c in text)
            {
                if (c != '\r' && c != '\n' && c != '\x1A' && (c < 0x20 || c > 0x7E))
                {
                    return false;
                }
            }
            var lines = text.Split('\n');
            int i = 0;
            while (i < lines.Length && lines[i].Length == 0) i++;
            if (i >= lines.Length)
            {
                return false;
            }
            // header line: DDDD:hex
            string head = lines[i].Trim();
            if (head.Length < 6 || head[4] != ':')
            {
                return false;
            }
            for (int k = 0; k < 4; k++)
            {
                if (!char.IsDigit(head[k]))
                {
                    return false;
                }
            }
            foreach (char c in head.Substring(5))
            {
                if (!IsHex(c))
                {
                    return false;
                }
            }
            image.HeaderCode = head.Substring(0, 4);
            image.HeaderPayload = head.Substring(5);
            i++;

            var chunks = new List<(long Addr, byte[] Data)>();
            long baseAddr = 0;
            bool eof = false;
            for (; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line == "\x1A")
                {
                    continue;
                }
                if (line.Length < 11 || line[0] != ':')
                {
                    return false;
                }
                for (int k = 1; k < line.Length; k++)
                {
                    if (!IsHex(line[k]))
                    {
                        return false;
                    }
                }
                if ((line.Length - 1) % 2 != 0)
                {
                    return false;
                }
                var raw = new byte[(line.Length - 1) / 2];
                for (int k = 0; k < raw.Length; k++)
                {
                    raw[k] = (byte)((HexNibble(line[1 + 2 * k]) << 4) | HexNibble(line[2 + 2 * k]));
                }
                int sum = 0;
                foreach (byte b in raw) sum += b;
                if ((sum & 0xFF) != 0)
                {
                    return false;
                }
                int count = raw[0];
                int addr = (raw[1] << 8) | raw[2];
                int type = raw[3];
                if (raw.Length != 5 + count)
                {
                    return false;
                }
                switch (type)
                {
                    case 0x00:
                        var data = new byte[count];
                        Array.Copy(raw, 4, data, 0, count);
                        chunks.Add((baseAddr + addr, data));
                        break;
                    case 0x01:
                        if (count != 0) return false;
                        eof = true;
                        break;
                    case 0x02:
                        if (count != 2) return false;
                        baseAddr = ((raw[4] << 8) | raw[5]) << 4;
                        break;
                    case 0x03:
                        break; // CS:IP start, informational
                    case 0x04:
                        if (count != 2) return false;
                        baseAddr = (long)((raw[4] << 8) | raw[5]) << 16;
                        break;
                    case 0x05:
                        if (count != 4) return false;
                        image.StartAddress = ((long)raw[4] << 24) | ((long)raw[5] << 16)
                            | ((long)raw[6] << 8) | raw[7];
                        image.HasStartAddress = true;
                        break;
                    default:
                        return false;
                }
            }
            if (!eof || chunks.Count == 0)
            {
                return false;
            }
            chunks.Sort((a, b) => a.Addr.CompareTo(b.Addr));
            // merge contiguous chunks into segments
            Segment? cur = null;
            foreach (var (addr, data) in chunks)
            {
                if (cur != null && addr == cur.Start + cur.Data.Length)
                {
                    var grown = new byte[cur.Data.Length + data.Length];
                    Array.Copy(cur.Data, grown, cur.Data.Length);
                    Array.Copy(data, 0, grown, cur.Data.Length, data.Length);
                    cur.Data = grown;
                }
                else
                {
                    cur = new Segment { Start = addr, Data = (byte[])data.Clone() };
                    image.Segments.Add(cur);
                }
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static byte[] ToImage(Image image, out long baseAddress)
    {
        long lo = long.MaxValue;
        long hi = 0;
        foreach (var s in image.Segments)
        {
            lo = Math.Min(lo, s.Start);
            hi = Math.Max(hi, s.Start + s.Data.Length);
        }
        baseAddress = lo;
        var flat = new byte[hi - lo];
        for (long i = 0; i < flat.Length; i++)
        {
            flat[i] = 0xFF;
        }
        foreach (var s in image.Segments)
        {
            Array.Copy(s.Data, 0, flat, s.Start - lo, s.Data.Length);
        }
        return flat;
    }
}
