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

namespace Yafex.FileSystems.Ubi;

/// <summary>A single UBI volume: id, name, and its LEB map (lnum to data).</summary>
public sealed class UbiVolume
{
    public uint VolId { get; }
    public string Name { get; }
    public byte VolType { get; }
    public int LebSize { get; }

    // lnum -> LEB data bytes (data area only, headers stripped)
    private readonly SortedDictionary<uint, byte[]> _lebs = new();

    internal UbiVolume(uint volId, string name, byte volType, int lebSize)
    {
        VolId = volId;
        Name = name;
        VolType = volType;
        LebSize = lebSize;
    }

    internal void AddLeb(uint lnum, byte[] data) => _lebs[lnum] = data;

    public IReadOnlyDictionary<uint, byte[]> Lebs => _lebs;

    public uint LebCount => _lebs.Count == 0 ? 0 : (uint)(MaxLnum + 1);

    private uint MaxLnum
    {
        get
        {
            uint max = 0;
            foreach (uint k in _lebs.Keys)
            {
                if (k > max) max = k;
            }
            return max;
        }
    }

    /// <summary>
    /// Volume payload: LEB data areas concatenated in lnum order.
    /// Unmapped LEBs read as zeros so offsets stay lnum-aligned.
    /// </summary>
    public byte[] Payload()
    {
        uint count = LebCount;
        var out_ = new byte[count * LebSize];
        foreach (var (lnum, data) in _lebs)
        {
            int take = Math.Min(data.Length, LebSize);
            Array.Copy(data, 0, out_, (long)lnum * LebSize, take);
        }
        return out_;
    }
}

/// <summary>
/// Read-only UBI reader over a raw image region: scans eraseblocks,
/// validates EC/VID headers, and groups LEBs into volumes. Volume names
/// come from the layout volume's table (ASCII-name variant, with a stock
/// big-endian-name fallback); unknown volumes keep numeric names.
/// </summary>
public sealed class UbiReader
{
    private readonly List<UbiVolume> _volumes = new();

    public IReadOnlyList<UbiVolume> Volumes => _volumes;

    public long BaseOffset { get; }

    public int PebSize { get; }

    public int DataOffset { get; }

    private UbiReader(long baseOffset, int pebSize, int dataOffset)
    {
        BaseOffset = baseOffset;
        PebSize = pebSize;
        DataOffset = dataOffset;
    }

    public static UbiReader? Open(byte[] image, long baseOffset)
    {
        if (!UbiConstants.ValidEc(image, baseOffset)) return null;
        uint dataOff = UbiConstants.ReadU32BE(image, baseOffset + 20);
        // PEB size from the next EC header; single-PEB fallback spans the rest
        long pebSize = image.Length - baseOffset;
        long scan = baseOffset + 512;
        long limit = Math.Min(image.Length - 4, baseOffset + (1L << 26));
        for (long pos = scan; pos < limit; pos++)
        {
            if (image[pos] == (byte)'U' && image[pos + 1] == (byte)'B'
                && image[pos + 2] == (byte)'I' && image[pos + 3] == (byte)'#'
                && UbiConstants.ValidEc(image, pos))
            {
                pebSize = pos - baseOffset;
                break;
            }
        }
        if (pebSize < dataOff + 512 || pebSize > 1 << 26) return null;
        int lebSize = (int)(pebSize - dataOff);

        var reader = new UbiReader(baseOffset, (int)pebSize, (int)dataOff);
        var byVol = new SortedDictionary<uint, UbiVolume>();
        for (long peb = baseOffset; peb + pebSize <= image.Length; peb += pebSize)
        {
            if (!UbiConstants.ValidEc(image, peb)) continue;
            long vidOff = peb + UbiConstants.ReadU32BE(image, peb + 16);
            if (!UbiConstants.ValidVid(image, vidOff)) continue;
            uint volId = UbiConstants.ReadU32BE(image, vidOff + 8);
            uint lnum = UbiConstants.ReadU32BE(image, vidOff + 12);
            byte volType = image[vidOff + 5];
            if (!byVol.TryGetValue(volId, out var vol))
            {
                vol = new UbiVolume(volId, $"vol{volId}", volType, lebSize);
                byVol[volId] = vol;
            }
            long dataStart = peb + dataOff;
            int len = (int)Math.Min(lebSize, image.Length - dataStart);
            if (len <= 0) continue;
            var data = new byte[len];
            Array.Copy(image, dataStart, data, 0, len);
            vol.AddLeb(lnum, data);
        }
        if (byVol.Count == 0) return null;
        reader.ApplyNames(byVol);
        foreach (var vol in byVol.Values)
        {
            reader._volumes.Add(vol);
        }
        return reader;
    }

    private void ApplyNames(SortedDictionary<uint, UbiVolume> byVol)
    {
        // layout volume: highest internal id that parses as a table
        var ids = new List<uint>(byVol.Keys);
        ids.Sort();
        for (int i = ids.Count - 1; i >= 0; i--)
        {
            if (ids[i] < 0x7F00) break;
            if (TryNameAscii(byVol[ids[i]], byVol)) return;
            if (TryNameStock(byVol[ids[i]], byVol)) return;
        }
    }

    private static bool TryNameAscii(UbiVolume layout, SortedDictionary<uint, UbiVolume> byVol)
    {
        if (!layout.Lebs.TryGetValue(0, out var table)) return false;
        bool any = false;
        var names = new Dictionary<uint, string>();
        for (int r = 0; r * UbiConstants.VtblRecordStride + 32 <= table.Length
            && names.Count < UbiConstants.MaxVolumes; r++)
        {
            int at = r * UbiConstants.VtblRecordStride;
            byte volType = table[at + UbiConstants.VtblTypeOff];
            int nameLen = table[at + UbiConstants.VtblNameLenOff];
            if (volType == 0 && nameLen == 0) continue; // empty slot
            if (volType > 2 || nameLen < 1 || nameLen > 127) break;
            if (at + UbiConstants.VtblNameOff + nameLen > table.Length) break;
            bool printable = true;
            for (int k = 0; k < nameLen; k++)
            {
                byte c = table[at + UbiConstants.VtblNameOff + k];
                if (c < 0x20 || c >= 0x7F) { printable = false; break; }
            }
            if (!printable) break;
            string name = Encoding.ASCII.GetString(
                table, at + UbiConstants.VtblNameOff, nameLen);
            names[(uint)names.Count] = name;
            any = true;
        }
        // records run in volume-id order from zero
        foreach (var (id, name) in names)
        {
            if (byVol.TryGetValue(id, out var vol) && vol.VolId != layout.VolId)
            {
                byVol[id] = Rename(vol, name);
            }
        }
        return any;
    }

    private static bool TryNameStock(UbiVolume layout, SortedDictionary<uint, UbiVolume> byVol)
    {
        // stock big-endian u16-name table: type/flags/namelen bytes plus
        // a 256-byte BE name field (kernel ubi-media layout)
        if (!layout.Lebs.TryGetValue(0, out var table)) return false;
        const int stride = 4 + 256 + 32;
        bool any = false;
        var names = new Dictionary<uint, string>();
        for (int r = 0; r * stride + 260 <= table.Length
            && names.Count < UbiConstants.MaxVolumes; r++)
        {
            int at = r * stride;
            byte volType = table[at];
            int nameLen = table[at + 2];
            if (volType == 0 && nameLen == 0) continue;
            if (volType > 2 || nameLen < 1 || nameLen > 127) break;
            var sb = new StringBuilder();
            bool printable = true;
            for (int k = 0; k < nameLen; k++)
            {
                int hi = table[at + 4 + 2 * k];
                int lo = table[at + 4 + 2 * k + 1];
                if (hi != 0 || lo < 0x20 || lo >= 0x7F) { printable = false; break; }
                sb.Append((char)lo);
            }
            if (!printable) break;
            names[(uint)names.Count] = sb.ToString();
            any = true;
        }
        foreach (var (id, name) in names)
        {
            if (byVol.TryGetValue(id, out var vol) && vol.VolId != layout.VolId)
            {
                byVol[id] = Rename(vol, name);
            }
        }
        return any;
    }

    private static UbiVolume Rename(UbiVolume vol, string name)
    {
        var renamed = new UbiVolume(vol.VolId, name, vol.VolType, vol.LebSize);
        foreach (var (lnum, data) in vol.Lebs)
        {
            renamed.AddLeb(lnum, data);
        }
        return renamed;
    }
}
