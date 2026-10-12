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

namespace Yafex.FileFormats.AmdPsp;

/// <summary>
/// AMD SPI flash structures: Embedded Firmware Structure (EFS, signature
/// 0x55AA55AA with pointers to the PSP directory) plus PSP/BIOS directory
/// tables ($PSP/$BHD with level-2 $PL2/$BL2 secondaries).
/// Directory integrity is fletcher32 over everything after the checksum
/// field (amdfwtool convention); random magic byte-hits (e.g. inside ARM
/// firmware blobs) fail validation and are ignored.
/// References: coreboot util/amdfwtool/amdfwtool.h, src/vendorcode/amd/psp
/// (psp_directory.h, bios_directory.h, combo_directory.h).
/// </summary>
public static class AmdPsp
{
    public const uint EfsSignature = 0x55AA55AA;
    public const int EfsPointerScanLength = 0x50;

    public const uint PspCookie = 0x50535024; // '$PSP'
    public const uint BhdCookie = 0x44484224; // '$BHD' ("DHBD" little-endian)
    public const uint Pl2Cookie = 0x324C5024; // '$PL2'
    public const uint Bl2Cookie = 0x324C4224; // '$BL2'

    public const int DirHeaderSize = 16;
    public const int PspEntrySize = 16;
    public const int BiosEntrySize = 24;
    public const int MaxEntries = 4096;

    public enum DirKind
    {
        Psp,
        Bios,
    }

    public sealed class Entry
    {
        public byte Type;
        public string TypeName = "";
        public uint Size;
        public ulong AddrRaw;
        public int AddrMode;
        public long ResolvedOffset = -1;
        public long Avail;
        public byte Flags;
        public byte SubProgram;
    }

    public sealed class Directory
    {
        public long Offset;
        public DirKind Kind;
        public string CookieName = "";
        public uint NumEntries;
        public List<Entry> Entries = new();
    }

    public sealed class Efs
    {
        public long Offset;
        // raw u32 pointer fields that resolve to validated directories
        public List<(int FieldOffset, long DirOffset)> ResolvedPointers = new();
    }

    public static uint Fletcher32(byte[] buf, int offset, int length)
    {
        uint sum1 = 0xFFFF;
        uint sum2 = 0xFFFF;
        int end = offset + (length & ~1);
        for (int i = offset; i < end; i += 2)
        {
            uint w = (uint)(buf[i] | (buf[i + 1] << 8));
            sum1 = (sum1 + w) % 0xFFFF;
            sum2 = (sum2 + sum1) % 0xFFFF;
        }
        if ((length & 1) != 0)
        {
            sum1 = (sum1 + buf[offset + length - 1]) % 0xFFFF;
            sum2 = (sum2 + sum1) % 0xFFFF;
        }
        return (sum2 << 16) | sum1;
    }

    private static uint ReadU32(byte[] buf, int off)
    {
        return (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));
    }

    private static ulong ReadU64(byte[] buf, int off)
    {
        return (ulong)ReadU32(buf, off) | ((ulong)ReadU32(buf, off + 4) << 32);
    }

    private static readonly Dictionary<byte, string> PspTypeNames = new()
    {
        [0x00] = "amd_public_key",
        [0x01] = "psp_fw_boot_loader",
        [0x02] = "psp_fw_trusted",
        [0x03] = "psp_fw_recovery",
        [0x04] = "smu_offchip_fw",
        [0x08] = "smu_fw",
        [0x12] = "smu_fw2",
        [0x1B] = "psp_boot_driver",
        [0x1C] = "psp_soc_driver",
        [0x1D] = "psp_debug_driver",
        [0x1F] = "psp_interface_driver",
        [0x20] = "psp_hw_ipcfg",
        [0x21] = "psp_wrapped_ikek",
        [0x22] = "psp_token_unlock",
        [0x24] = "psp_sec_gasket",
        [0x25] = "psp_mp2_fw",
        [0x28] = "psp_driver_entries",
        [0x29] = "psp_kvm_image",
        [0x2A] = "psp_mp5",
        [0x2D] = "psp_s0i3_driver",
        [0x30] = "abl0",
        [0x31] = "abl1",
        [0x32] = "abl2",
        [0x33] = "abl3",
        [0x34] = "abl4",
        [0x35] = "abl5",
        [0x36] = "abl6",
        [0x37] = "abl7",
        [0x38] = "sev_data",
        [0x39] = "sev_code",
        [0x3A] = "whitelist",
        [0x3C] = "vbios_btloader",
        [0x40] = "psp_l2_ptr",
        [0x42] = "dxio",
        [0x44] = "usb_phy",
        [0x45] = "tos_sec_policy",
        [0x47] = "drtm_ta",
        [0x48] = "recoveryab_a",
        [0x49] = "bios_table",
        [0x4A] = "recoveryab_b",
        [0x50] = "keydb_bl",
        [0x51] = "keydb_tos",
        [0x52] = "verstage",
        [0x53] = "verstage_sig",
        [0x54] = "rpmc_nvram",
        [0x55] = "spl",
        [0x58] = "dmcu_eram",
        [0x59] = "dmcu_isr",
        [0x5A] = "msmu",
        [0x5C] = "spirom_cfg",
        [0x5D] = "mpio",
        [0x5F] = "tpm_lite",
        [0x64] = "ras_driver",
        [0x65] = "ras_ta",
        [0x67] = "fhp_driver",
        [0x68] = "spdm_driver",
        [0x69] = "dpe_driver",
        [0x6E] = "ab_nvram",
        [0x71] = "dmcub",
        [0x73] = "bootloader_ab",
        [0x76] = "rib",
        [0x85] = "amf_sram",
        [0x86] = "amf_dram",
        [0x87] = "mfd_mpm",
        [0x8E] = "sfdr",
    };

    private static readonly Dictionary<byte, string> BiosTypeNames = new()
    {
        [0x05] = "rtm_pubkey",
        [0x07] = "sig",
        [0x60] = "apcb",
        [0x61] = "apob",
        [0x62] = "bios_bin",
        [0x63] = "apob_nv",
        [0x64] = "pmu_i",
        [0x65] = "pmu_d",
        [0x66] = "ucode",
        [0x68] = "apcb_bk",
        [0x69] = "early_vga",
        [0x6A] = "mp2_cfg",
        [0x6D] = "nv_store",
        [0x70] = "l2_ptr",
    };

    public static string PspTypeName(byte type)
    {
        return PspTypeNames.TryGetValue(type, out var name)
            ? name
            : $"t{type:X2}";
    }

    public static string BiosTypeName(byte type)
    {
        return BiosTypeNames.TryGetValue(type, out var name)
            ? name
            : $"t{type:X2}";
    }

    /// <summary>
    /// Resolve an entry address to a file offset. Mode 0: x86 physical
    /// (flash mapped at top of 4GB), mode 1: flash offset, mode 2:
    /// relative to the containing directory. Returns -1 when unresolvable.
    /// </summary>
    public static long ResolveAddress(ulong addrRaw, long dirBase, long fileLength)
    {
        int mode = (int)(addrRaw >> 62);
        long addr = (long)(addrRaw & ((1UL << 62) - 1));
        long off = mode switch
        {
            0 => addr - (0x100000000L - fileLength),
            1 => addr,
            2 => dirBase + addr,
            _ => -1,
        };
        return off < 0 || off >= fileLength ? -1 : off;
    }

    public static bool TryReadDirectory(byte[] image, long offset, out Directory dir)
    {
        dir = new Directory();
        try
        {
            if (offset < 0 || offset + DirHeaderSize > image.Length)
            {
                return false;
            }
            int o = (int)offset;
            uint cookie = ReadU32(image, o);
            DirKind kind;
            int entrySize;
            string cookieName;
            if (cookie == PspCookie || cookie == Pl2Cookie)
            {
                kind = DirKind.Psp;
                entrySize = PspEntrySize;
                cookieName = cookie == PspCookie ? "$PSP" : "$PL2";
            }
            else if (cookie == BhdCookie || cookie == Bl2Cookie)
            {
                kind = DirKind.Bios;
                entrySize = BiosEntrySize;
                cookieName = cookie == BhdCookie ? "$BHD" : "$BL2";
            }
            else
            {
                return false;
            }
            uint checksum = ReadU32(image, o + 4);
            uint numEntries = ReadU32(image, o + 8);
            if (numEntries < 1 || numEntries > MaxEntries)
            {
                return false;
            }
            long tableEnd = offset + DirHeaderSize + (long)numEntries * entrySize;
            if (tableEnd > image.Length)
            {
                return false;
            }
            uint want = Fletcher32(image, o + 8, (int)(8 + numEntries * entrySize));
            if (want != checksum)
            {
                return false;
            }
            var entries = new List<Entry>();
            for (uint i = 0; i < numEntries; i++)
            {
                int e = o + DirHeaderSize + (int)i * entrySize;
                if (kind == DirKind.Psp)
                {
                    byte type = image[e];
                    byte sub = image[e + 1];
                    uint size = ReadU32(image, e + 4);
                    ulong addrRaw = ReadU64(image, e + 8);
                    long resolved = ResolveAddress(addrRaw, offset, image.Length);
                    long avail = resolved < 0 ? 0 : Math.Min((long)size, image.Length - resolved);
                    entries.Add(new Entry
                    {
                        Type = type,
                        TypeName = PspTypeName(type),
                        Size = size,
                        AddrRaw = addrRaw,
                        AddrMode = (int)(addrRaw >> 62),
                        ResolvedOffset = resolved,
                        Avail = size == 0 || size == 0xFFFFFFFF || resolved < 0 ? 0 : avail,
                        SubProgram = sub,
                    });
                }
                else
                {
                    byte type = image[e];
                    byte flags = image[e + 2];
                    byte sub = image[e + 3];
                    uint size = ReadU32(image, e + 4);
                    ulong srcRaw = ReadU64(image, e + 8);
                    long resolved = ResolveAddress(srcRaw, offset, image.Length);
                    long avail = resolved < 0 ? 0 : Math.Min((long)size, image.Length - resolved);
                    entries.Add(new Entry
                    {
                        Type = type,
                        TypeName = BiosTypeName(type),
                        Size = size,
                        AddrRaw = srcRaw,
                        AddrMode = (int)(srcRaw >> 62),
                        ResolvedOffset = resolved,
                        Avail = size == 0 || size == 0xFFFFFFFF || resolved < 0 ? 0 : avail,
                        Flags = flags,
                        SubProgram = sub,
                    });
                }
            }
            dir = new Directory
            {
                Offset = offset,
                Kind = kind,
                CookieName = cookieName,
                NumEntries = numEntries,
                Entries = entries,
            };
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>All checksum-validated directories in the image, in file order.</summary>
    public static List<Directory> FindDirectories(byte[] image)
    {
        var dirs = new List<Directory>();
        try
        {
            int pos = 0;
            while (pos + 4 <= image.Length)
            {
                int i = IndexOf(image, pos);
                if (i < 0)
                {
                    break;
                }
                pos = i + 4;
                if (TryReadDirectory(image, i, out var dir))
                {
                    dirs.Add(dir);
                    // skip past this table so nested magics inside entries
                    // (e.g. entry blobs) are not re-scanned as tables
                    long end = dir.Offset + DirHeaderSize
                        + (long)dir.NumEntries * (dir.Kind == DirKind.Psp ? PspEntrySize : BiosEntrySize);
                    if (end > pos)
                    {
                        pos = (int)end;
                    }
                }
            }
        }
        catch (Exception)
        {
        }
        return dirs;
    }

    private static int IndexOf(byte[] image, int start)
    {
        // next '$' that begins a known directory cookie
        for (int i = start; i + 4 <= image.Length; i++)
        {
            if (image[i] != 0x24)
            {
                continue;
            }
            uint cookie = ReadU32(image, i);
            if (cookie == PspCookie || cookie == BhdCookie || cookie == Pl2Cookie || cookie == Bl2Cookie)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// EFS instances whose pointer fields resolve to validated directories.
    /// Only the signature is positional; pointers are accepted wherever a u32
    /// field inside the EFS body resolves (known: +0x14 PSP dir).
    /// </summary>
    public static List<Efs> FindEfs(byte[] image)
    {
        var list = new List<Efs>();
        try
        {
            int pos = 0;
            while (pos + 4 <= image.Length)
            {
                int i = -1;
                for (int j = pos; j + 4 <= image.Length; j++)
                {
                    if (ReadU32(image, j) == EfsSignature)
                    {
                        i = j;
                        break;
                    }
                }
                if (i < 0)
                {
                    break;
                }
                pos = i + 4;
                if (i + EfsPointerScanLength > image.Length)
                {
                    continue;
                }
                var efs = new Efs { Offset = i };
                for (int f = 4; f + 4 <= EfsPointerScanLength; f += 4)
                {
                    uint ptr = ReadU32(image, i + f);
                    if (ptr == 0 || ptr >= image.Length)
                    {
                        continue;
                    }
                    if (TryReadDirectory(image, ptr, out _))
                    {
                        efs.ResolvedPointers.Add((f, ptr));
                    }
                }
                if (efs.ResolvedPointers.Count > 0)
                {
                    list.Add(efs);
                }
            }
        }
        catch (Exception)
        {
        }
        return list;
    }
}
