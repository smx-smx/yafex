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

namespace Yafex.FileSystems.MtkBdp;

/// <summary>
/// MediaTek Blu-ray update image tables (PITIT/PIT/BIT), as validated
/// against the updater logic left in the images (boot_1 carries the
/// "Find PIT from nand" strings): PITIT points at per-NAND-size partition
/// tables (PIT, "DC EA 30 85" x2), BIT ("CD AB 30 85" x5, entries until
/// the EF EF 30 85 terminator) maps update-file ranges into NAND
/// partitions, cross-referenced by partition id. No checksums on the
/// tables; strength comes from chained magic, bounds, printable names,
/// and id cross-match validation.
/// </summary>
public static class MtkBdpConstants
{
    public static readonly byte[] PititMagic = { 0x69, 0x54, 0x49, 0x50, 0x69, 0x54, 0x49, 0x50 };
    public static readonly byte[] PitMagic = { 0xDC, 0xEA, 0x30, 0x85, 0xDC, 0xEA, 0x30, 0x85 };
    public static readonly byte[] BitMagic = {
        0xCD, 0xAB, 0x30, 0x85, 0xCD, 0xAB, 0x30, 0x85, 0xCD, 0xAB, 0x30, 0x85,
        0xCD, 0xAB, 0x30, 0x85, 0xCD, 0xAB, 0x30, 0x85 };
    public const uint BitEndId = 0x8530EFEF;
    public const int MaxPititEntries = 10;
    public const int MaxPitEntries = 128;
    public const int MaxBitEntries = 1024;

    public static uint ReadU32(byte[] buf, long off)
    {
        return (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));
    }

    public static bool IsPrintableAscii(string s)
    {
        if (s.Length == 0 || s.Length > 16) return false;
        foreach (char c in s)
        {
            if (c < 0x20 || c >= 0x7F) return false;
        }
        return true;
    }

    public static string ReadName(byte[] image, long offset)
    {
        int len = 0;
        while (len < 16 && offset + len < image.Length && image[offset + len] != 0) len++;
        return Encoding.ASCII.GetString(image, (int)offset, len);
    }
}

/// <summary>A NAND partition from PIT: id, name, and NAND geometry.</summary>
public sealed class MtkBdpPartition
{
    public uint Id;
    public string Name = string.Empty;
    public uint NandOffset;
    public uint NandSize;
}

/// <summary>One update-file range belonging to a partition.</summary>
public sealed class MtkBdpChunk
{
    public uint PartId;
    public uint FileOffset;
    public uint Size;
    public uint InPartOffset;
    public uint Flags;
}

/// <summary>An assembled partition: PIT info plus its ordered chunks.</summary>
public sealed class MtkBdpImage
{
    public MtkBdpPartition Part = new();
    public List<MtkBdpChunk> Chunks = new();

    /// <summary>Partition payload assembled in partition-offset order.</summary>
    public byte[] Assemble(byte[] image)
    {
        long total = 0;
        foreach (var c in Chunks)
        {
            total = Math.Max(total, (long)c.InPartOffset + c.Size);
        }
        var out_ = new byte[total];
        var ordered = new List<MtkBdpChunk>(Chunks);
        ordered.Sort((a, b) => a.InPartOffset.CompareTo(b.InPartOffset));
        foreach (var c in ordered)
        {
            long take = Math.Min(c.Size, image.Length - c.FileOffset);
            if (take <= 0) continue;
            Array.Copy(image, c.FileOffset, out_, c.InPartOffset, take);
        }
        return out_;
    }
}

public static class MtkBdpParser
{
    public static long FindBytes(byte[] image, byte[] pattern, long start = 0)
    {
        for (long i = start; i + pattern.Length <= image.Length; i++)
        {
            bool hit = true;
            for (int k = 0; k < pattern.Length; k++)
            {
                if (image[i + k] != pattern[k]) { hit = false; break; }
            }
            if (hit) return i;
        }
        return -1;
    }

    private static bool ValidPit(byte[] image, long offset, out List<MtkBdpPartition> parts)
    {
        parts = new List<MtkBdpPartition>();
        if (offset < 0 || offset + 24 > image.Length) return false;
        uint first = MtkBdpConstants.ReadU32(image, offset + 12);
        uint esize = MtkBdpConstants.ReadU32(image, offset + 16);
        uint ecount = MtkBdpConstants.ReadU32(image, offset + 20);
        if (esize < 32 || esize > 256 || ecount == 0
            || ecount > MtkBdpConstants.MaxPitEntries) return false;
        if (offset + first + (long)esize * ecount > image.Length) return false;
        for (uint i = 0; i < ecount; i++)
        {
            long at = offset + first + i * esize;
            string name = MtkBdpConstants.ReadName(image, at);
            if (!MtkBdpConstants.IsPrintableAscii(name)) return false;
            parts.Add(new MtkBdpPartition
            {
                Name = name,
                Id = MtkBdpConstants.ReadU32(image, at + 16),
                NandOffset = MtkBdpConstants.ReadU32(image, at + 24),
                NandSize = MtkBdpConstants.ReadU32(image, at + 28),
            });
        }
        return parts.Count > 0;
    }

    private static bool ValidBit(byte[] image, long offset, HashSet<uint> ids,
        out List<MtkBdpChunk> chunks)
    {
        chunks = new List<MtkBdpChunk>();
        long at = offset + MtkBdpConstants.BitMagic.Length;
        for (int i = 0; i < MtkBdpConstants.MaxBitEntries; i++)
        {
            if (at + 20 > image.Length) return false;
            uint id = MtkBdpConstants.ReadU32(image, at);
            if (id == MtkBdpConstants.BitEndId) break;
            uint fileOff = MtkBdpConstants.ReadU32(image, at + 4);
            uint size = MtkBdpConstants.ReadU32(image, at + 8);
            if ((long)fileOff + size > image.Length) return false;
            chunks.Add(new MtkBdpChunk
            {
                PartId = id,
                FileOffset = fileOff,
                Size = size,
                InPartOffset = MtkBdpConstants.ReadU32(image, at + 12),
                Flags = MtkBdpConstants.ReadU32(image, at + 16),
            });
            at += 20;
        }
        if (chunks.Count == 0) return false;
        foreach (var c in chunks)
        {
            if (ids.Contains(c.PartId)) return true;
        }
        return false;
    }

    /// <summary>
    /// Open an update image: PITIT scan, PIT validation, BIT validation
    /// with id cross-match, chunk grouping. Returns null when nothing
    /// validates.
    /// </summary>
    public static List<MtkBdpImage>? Open(byte[] image)
    {
        long pitit = FindBytes(image, MtkBdpConstants.PititMagic);
        while (pitit >= 0)
        {
            var result = TryPitit(image, pitit);
            if (result != null) return result;
            pitit = FindBytes(image, MtkBdpConstants.PititMagic, pitit + 1);
        }
        return null;
    }

    private static List<MtkBdpImage>? TryPitit(byte[] image, long pitit)
    {
        List<MtkBdpPartition>? bestParts = null;
        for (int e = 0; e < MtkBdpConstants.MaxPititEntries; e++)
        {
            long at = pitit + 16 + e * 16;
            if (at + 16 > image.Length) break;
            uint pitOff = MtkBdpConstants.ReadU32(image, at + 4);
            uint pitSize = MtkBdpConstants.ReadU32(image, at + 8);
            if (pitSize == 0) continue;
            if ((long)pitOff + pitSize > image.Length) continue;
            if (!StartsWith(image, pitOff, MtkBdpConstants.PitMagic)) continue;
            if (!ValidPit(image, pitOff, out var parts)) continue;
            if (bestParts == null || parts.Count > bestParts.Count) bestParts = parts;
        }
        if (bestParts == null) return null;
        var ids = new HashSet<uint>();
        foreach (var p in bestParts) ids.Add(p.Id);
        long bit = FindBytes(image, MtkBdpConstants.BitMagic);
        List<MtkBdpChunk>? chunks = null;
        while (bit >= 0)
        {
            if (ValidBit(image, bit, ids, out var c)) { chunks = c; break; }
            bit = FindBytes(image, MtkBdpConstants.BitMagic, bit + 1);
        }
        if (chunks == null) return null;
        var images = new List<MtkBdpImage>();
        foreach (var p in bestParts)
        {
            var img = new MtkBdpImage { Part = p };
            foreach (var c in chunks)
            {
                if (c.PartId == p.Id) img.Chunks.Add(c);
            }
            if (img.Chunks.Count > 0) images.Add(img);
        }
        return images.Count > 0 ? images : null;
    }

    private static bool StartsWith(byte[] image, long offset, byte[] pattern)
    {
        if (offset < 0 || offset + pattern.Length > image.Length) return false;
        for (int k = 0; k < pattern.Length; k++)
        {
            if (image[offset + k] != pattern[k]) return false;
        }
        return true;
    }
}
