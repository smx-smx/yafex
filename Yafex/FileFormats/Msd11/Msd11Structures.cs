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
using System.Text;

namespace Yafex.FileFormats.Msd11;

public sealed record Msd11Section(uint Index, long Offset, long Size);

public sealed record Msd11HeaderEntry(string Name, long Offset, int Size);

/// <summary>One firmware member: a stored section plus its TOC identity.
/// Encrypted members are AES-CBC (Tizen salted-IV derivation) with PKCS7
/// padding, so the stored size always exceeds the payload by 1..16 bytes.</summary>
public sealed record Msd11Member(
    uint Index, string Name, long Offset, long Size, bool Encrypted, byte[]? Salt);

/// <summary>Samsung MSDU11 container layout: magic, checksum/size/count,
/// fixed 20-byte section entries (index, offset, size), then a header-entry
/// table whose first entry wraps the encrypted OUITH table of contents.</summary>
public static class Msd11Structures
{
    public const string Magic = "MSDU11";
    public const int SectionEntrySize = 20;
    public const int MinHeaderSize = 6 + 4 + 8 + 4;

    public static bool TryParseTableOfContents(
        byte[] prefix, long totalLength, out List<Msd11Section> sections, out List<Msd11HeaderEntry> headers)
    {
        sections = new List<Msd11Section>();
        headers = new List<Msd11HeaderEntry>();
        try
        {
            if (prefix.Length < MinHeaderSize) return false;
            if (Encoding.ASCII.GetString(prefix, 0, 6) != Magic) return false;
            uint sectionCount = BitConverter.ToUInt32(prefix, 18);
            if (sectionCount == 0 || sectionCount > 100000) return false;

            long pos = 22;
            for (long i = 0; i < sectionCount; i++)
            {
                if (pos + SectionEntrySize > prefix.Length) return false;
                uint index = BitConverter.ToUInt32(prefix, (int)pos);
                ulong offset = BitConverter.ToUInt64(prefix, (int)pos + 4);
                ulong size = BitConverter.ToUInt64(prefix, (int)pos + 12);
                if (offset + size < offset || offset + size > (ulong)totalLength) return false;
                sections.Add(new Msd11Section(index, (long)offset, (long)size));
                pos += SectionEntrySize;
            }

            if (pos + 4 > prefix.Length) return false;
            uint headerCount = BitConverter.ToUInt32(prefix, (int)pos);
            if (headerCount == 0 || headerCount > 100000) return false;
            pos += 4;
            for (long i = 0; i < headerCount; i++)
            {
                if (pos + 13 > prefix.Length) return false;
                ulong offset = BitConverter.ToUInt64(prefix, (int)pos);
                uint size = BitConverter.ToUInt32(prefix, (int)pos + 8);
                int nameLen = prefix[(int)pos + 12];
                if (pos + 13 + nameLen > prefix.Length) return false;
                if (offset + size < offset || offset + size > (ulong)totalLength) return false;
                string name = Encoding.ASCII.GetString(prefix, (int)pos + 13, nameLen);
                headers.Add(new Msd11HeaderEntry(name, (long)offset, (int)size));
                pos += 13 + nameLen;
            }
            if (headers.Count == 0) return false;
            // The TOC blob itself must sit inside the prefix: detection must
            // not fault GBs of payload into RAM.
            var toc = headers[0];
            if (toc.Offset + toc.Size > prefix.Length) return false;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
