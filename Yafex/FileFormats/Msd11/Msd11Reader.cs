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

namespace Yafex.FileFormats.Msd11;

public sealed record Msd11Context(
    List<Msd11Member> Members, Msd11ImageInfo? Info, string FirmwareName, byte[] Key);

/// <summary>Full MSDU11 open: section/header tables, TOC decryption with the
/// first passphrase that validates, and the TOC-to-section member mapping.
/// Members stay stored (decryption happens per read, not here).</summary>
public static class Msd11Reader
{
    public static Msd11Context? Open(byte[] file, IEnumerable<byte[]> keys)
    {
        return OpenToc(file, file.Length, keys);
    }

    /// <summary>TOC-only open from a header prefix: section bounds check
    /// against <paramref name="totalLength"/> so detection never pages in
    /// member payloads.</summary>
    public static Msd11Context? OpenToc(byte[] prefix, long totalLength, IEnumerable<byte[]> keys)
    {
        if (!Msd11Structures.TryParseTableOfContents(prefix, totalLength, out var sections, out var headers))
        {
            return null;
        }
        var tocHeader = headers[0];
        if (tocHeader.Size < 8) return null;
        var tocBlob = new byte[tocHeader.Size - 8];
        Array.Copy(prefix, tocHeader.Offset + 8, tocBlob, 0, tocBlob.Length);

        foreach (var key in keys)
        {
            byte[] toc;
            try
            {
                toc = Msd11Crypto.DecryptSalted(tocBlob, key);
            }
            catch (Exception)
            {
                continue;
            }
            if (!Msd11Crypto.LooksLikeOuith(toc)) continue;
            if (!Msd11Toc.TryParse(toc, out var items, out var info)) continue;

            var byIndex = new Dictionary<uint, Msd11Section>();
            foreach (var section in sections) byIndex[section.Index] = section;
            var members = new List<Msd11Member>();
            bool complete = true;
            foreach (var item in items)
            {
                if (!byIndex.TryGetValue(item.ItemId, out var section))
                {
                    complete = false;
                    break;
                }
                members.Add(new Msd11Member(
                    item.ItemId, item.Name, section.Offset, section.Size,
                    item.Encrypted, item.Salt));
            }
            if (!complete) continue;
            return new Msd11Context(members, info, tocHeader.Name, key);
        }
        return null;
    }

    /// <summary>Stored member bytes decrypted (AES members) or sliced raw.</summary>
    public static byte[] ReadMember(byte[] file, Msd11Member member, byte[] key)
    {
        var stored = new byte[member.Size];
        Array.Copy(file, member.Offset, stored, 0, stored.Length);
        if (!member.Encrypted) return stored;
        if (member.Salt == null) throw new InvalidOperationException($"no salt for {member.Name}");
        return Msd11Crypto.DecryptRaw(stored, key, member.Salt);
    }
}
