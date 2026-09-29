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

namespace Yafex.FileFormats.Idb;

/// <summary>
/// LG IDB (image database, e.g. IDB_1366s_eu.lr): text header plus a table of
/// NUM_IMG 16-byte entries {unk1, unk2, size, offset} pointing at raw image
/// blobs. Zero-size entries are duplicates and skipped. Blobs are PNG,
/// LGI1 (decoded to PNG on extraction) or opaque vendor data.
/// </summary>
public static class Idb
{
    public const string VersionTag = "IDB_VERSION=";
    public const string Version = "1.1";
    public const string CountTag = "IDB_NUM_IMG=";
    public const string EndTag = "IDB_HDR_ENDMARK";
    public const int EntrySize = 16;

    public sealed class Entry
    {
        public uint Size;
        public uint Offset;
    }

    public sealed class Table
    {
        public int ImageCount;
        public List<Entry> Entries = new List<Entry>();
    }

    // Cheap head sniff shared with the mount expander.
    public static bool HasMagic(byte[] prefix)
    {
        var tag = Encoding.ASCII.GetBytes(VersionTag + Version + "\n");
        if (prefix.Length < tag.Length)
        {
            return false;
        }
        for (int i = 0; i < tag.Length; i++)
        {
            if (prefix[i] != tag[i])
            {
                return false;
            }
        }
        return true;
    }

    private static uint ReadU32LE(byte[] buf, int off)
    {
        return (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));
    }

    public static bool TryParse(byte[] image, out Table table)
    {
        table = new Table();
        try
        {
            if (!HasMagic(image))
            {
                return false;
            }
            // header lines: VERSION, NUM_IMG, ... , ENDMARK
            int pos = 0;
            int lineEnd = Array.IndexOf(image, (byte)'\n', pos);
            if (lineEnd < 0)
            {
                return false;
            }
            pos = lineEnd + 1;
            var countBytes = Encoding.ASCII.GetBytes(CountTag);
            bool haveCount = false;
            int tableStart = -1;
            while (pos < image.Length)
            {
                int end = Array.IndexOf(image, (byte)'\n', pos);
                if (end < 0)
                {
                    return false;
                }
                int len = end - pos;
                if (len >= countBytes.Length)
                {
                    bool match = true;
                    for (int i = 0; i < countBytes.Length; i++)
                    {
                        if (image[pos + i] != countBytes[i])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        string num = Encoding.ASCII.GetString(image, pos + countBytes.Length, len - countBytes.Length);
                        if (!int.TryParse(num.Trim(), out table.ImageCount) || table.ImageCount <= 0)
                        {
                            return false;
                        }
                        haveCount = true;
                    }
                }
                var endBytes = Encoding.ASCII.GetBytes(EndTag);
                if (len >= endBytes.Length)
                {
                    bool match = true;
                    for (int i = 0; i < endBytes.Length; i++)
                    {
                        if (image[pos + i] != endBytes[i])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        tableStart = end + 1;
                        break;
                    }
                }
                pos = end + 1;
                if (pos > 4096)
                {
                    return false;
                }
            }
            if (!haveCount || tableStart < 0)
            {
                return false;
            }
            long tableBytes = (long)table.ImageCount * EntrySize;
            if (tableStart + tableBytes > image.Length)
            {
                return false;
            }
            for (int i = 0; i < table.ImageCount; i++)
            {
                int off = tableStart + i * EntrySize;
                uint size = ReadU32LE(image, off + 8);
                uint ofs = ReadU32LE(image, off + 12);
                if (size > 0 && (long)ofs + size > image.Length)
                {
                    return false;
                }
                table.Entries.Add(new Entry { Size = size, Offset = ofs });
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
