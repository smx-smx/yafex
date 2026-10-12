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

namespace Yafex.FileFormats.AndroidBoot;

/// <summary>
/// Android boot image (boot.img/recovery.img) v0/v1/v2 parser.
/// Layout v0/v1/v2 (all page-aligned, header is 1 page):
/// header, kernel (n pages), ramdisk (m pages), second (o pages),
/// recovery_dtbo (p pages, v1/v2), dtb (q pages, v2).
/// v3/v4 use a different header and are not supported here.
/// Reference: platform/system/tools/mkbootimg/include/bootimg/bootimg.h
/// (BOOT_MAGIC="ANDROID!", BOOT_NAME_SIZE=16, BOOT_ARGS_SIZE=512,
/// BOOT_EXTRA_ARGS_SIZE=1024, V1=1648, V2=1660).
/// </summary>
public static class AndroidBoot
{
    public const string Magic = "ANDROID!";
    public const int MagicSize = 8;
    public const int NameSize = 16;
    public const int ArgsSize = 512;
    public const int ExtraArgsSize = 1024;
    public const int IdWords = 8;
    public const int IdSize = 32;

    public const int HeaderV0Size = 1632;
    public const int HeaderV1Size = 1648;
    public const int HeaderV2Size = 1660;

    public const uint DtbMagicBe = 0xD00DFEED;
    public const int DtbMinSize = 40;

    public sealed class Segment
    {
        public string Name = "";
        public long Offset;
        public long Size;
    }

    public sealed class Header
    {
        public uint KernelSize;
        public uint KernelAddr;
        public uint RamdiskSize;
        public uint RamdiskAddr;
        public uint SecondSize;
        public uint SecondAddr;
        public uint TagsAddr;
        public uint PageSize;
        public uint HeaderVersion;
        public uint OsVersion;
        public string Name = "";
        public string Cmdline = "";
        public byte[] Id = Array.Empty<byte>();
        public uint RecoveryDtboSize;
        public ulong RecoveryDtboOffset;
        public uint BootHeaderSize;
        public uint DtbSize;
        public ulong DtbAddr;

        public List<Segment> Segments = new();
        public long TotalSize;
    }

    private static uint ReadU32LE(byte[] buf, int off)
    {
        return (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));
    }

    private static ulong ReadU64LE(byte[] buf, int off)
    {
        uint lo = ReadU32LE(buf, off);
        uint hi = ReadU32LE(buf, off + 4);
        return ((ulong)hi << 32) | lo;
    }

    private static uint ReadU32BE(byte[] buf, int off)
    {
        return ((uint)buf[off] << 24) | ((uint)buf[off + 1] << 16) | ((uint)buf[off + 2] << 8) | buf[off + 3];
    }

    private static bool IsPowerOfTwo(uint v)
    {
        return v != 0 && (v & (v - 1)) == 0;
    }

    private static string ReadCStr(byte[] buf, int off, int maxLen)
    {
        int len = 0;
        while (len < maxLen && buf[off + len] != 0)
        {
            len++;
        }
        return Encoding.ASCII.GetString(buf, off, len);
    }

    public static bool HasMagic(byte[] prefix)
    {
        if (prefix.Length < MagicSize)
        {
            return false;
        }
        for (int i = 0; i < MagicSize; i++)
        {
            if (prefix[i] != (byte)Magic[i])
            {
                return false;
            }
        }
        return true;
    }

    public static bool TryReadHeader(byte[] image, out Header header)
    {
        header = new Header();
        try
        {
            if (image.Length < HeaderV0Size || !HasMagic(image))
            {
                return false;
            }
            uint kernelSize = ReadU32LE(image, 8);
            uint kernelAddr = ReadU32LE(image, 12);
            uint ramdiskSize = ReadU32LE(image, 16);
            uint ramdiskAddr = ReadU32LE(image, 20);
            uint secondSize = ReadU32LE(image, 24);
            uint secondAddr = ReadU32LE(image, 28);
            uint tagsAddr = ReadU32LE(image, 32);
            uint pageSize = ReadU32LE(image, 36);
            uint headerVersion = ReadU32LE(image, 40);
            uint osVersion = ReadU32LE(image, 44);

            if (!IsPowerOfTwo(pageSize) || pageSize < 512 || pageSize > 16384)
            {
                return false;
            }
            if (headerVersion > 2)
            {
                // v3/v4 use a different header layout
                return false;
            }
            if (image.Length < pageSize)
            {
                return false;
            }

            string name = ReadCStr(image, 48, NameSize);
            string cmdline = ReadCStr(image, 64, ArgsSize);
            var id = new byte[IdSize];
            Buffer.BlockCopy(image, 576, id, 0, IdSize);
            string extra = ReadCStr(image, 608, ExtraArgsSize);
            if (extra.Length > 0)
            {
                cmdline = cmdline.Length > 0 ? cmdline + " " + extra : extra;
            }

            uint recoverySize = 0;
            ulong recoveryOff = 0;
            uint bootHeaderSize = 0;
            uint dtbSize = 0;
            ulong dtbAddr = 0;

            if (headerVersion >= 1)
            {
                if (image.Length < HeaderV1Size)
                {
                    return false;
                }
                recoverySize = ReadU32LE(image, 1632);
                recoveryOff = ReadU64LE(image, 1636);
                bootHeaderSize = ReadU32LE(image, 1644);
                if (bootHeaderSize != HeaderV1Size && headerVersion == 1)
                {
                    return false;
                }
                if (headerVersion == 2)
                {
                    if (image.Length < HeaderV2Size)
                    {
                        return false;
                    }
                    dtbSize = ReadU32LE(image, 1648);
                    dtbAddr = ReadU64LE(image, 1652);
                    if (bootHeaderSize != HeaderV2Size)
                    {
                        return false;
                    }
                }
            }

            long Pages(long size)
            {
                return size == 0 ? 0 : (size + pageSize - 1) / pageSize;
            }

            long kPages = Pages(kernelSize);
            long rPages = Pages(ramdiskSize);
            long sPages = Pages(secondSize);
            long pPages = Pages(recoverySize);
            long qPages = Pages(dtbSize);

            long kOff = pageSize;
            long rOff = kOff + kPages * pageSize;
            long sOff = rOff + rPages * pageSize;
            long recOff = sOff + sPages * pageSize;
            // recovery_dtbo has an explicit file offset; honor it when sane,
            // otherwise fall back to the sequential layout above.
            long recUse = recOff;
            if (recoverySize > 0 && recoveryOff != 0)
            {
                if (recoveryOff > int.MaxValue || (long)recoveryOff + recoverySize > image.Length)
                {
                    return false;
                }
                recUse = (long)recoveryOff;
            }
            long dtbOff = recUse + pPages * pageSize;
            long end = dtbOff + qPages * pageSize;

            if (kernelSize == 0)
            {
                return false;
            }
            if (end > image.Length || end <= pageSize)
            {
                return false;
            }
            // trailing bytes past the computed end mean a corrupt size field
            // (padding inside the last page is fine, extra pages are not).
            // Be lenient: allow exact end or end within the last page?
            // Strict: end must match a page boundary inside the file and any
            // bytes past end must be zero padding of the last page only.
            // For now require end <= file length and no extra full pages.
            if (image.Length - end >= pageSize)
            {
                return false;
            }

            var segs = new List<Segment>();
            segs.Add(new Segment { Name = "kernel", Offset = kOff, Size = kernelSize });
            if (ramdiskSize > 0)
            {
                segs.Add(new Segment { Name = "ramdisk", Offset = rOff, Size = ramdiskSize });
            }
            if (secondSize > 0)
            {
                segs.Add(new Segment { Name = "second", Offset = sOff, Size = secondSize });
            }
            if (recoverySize > 0)
            {
                segs.Add(new Segment { Name = "recovery_dtbo", Offset = recUse, Size = recoverySize });
            }
            if (dtbSize > 0)
            {
                segs.Add(new Segment { Name = "dtb", Offset = dtbOff, Size = dtbSize });
            }

            header = new Header
            {
                KernelSize = kernelSize,
                KernelAddr = kernelAddr,
                RamdiskSize = ramdiskSize,
                RamdiskAddr = ramdiskAddr,
                SecondSize = secondSize,
                SecondAddr = secondAddr,
                TagsAddr = tagsAddr,
                PageSize = pageSize,
                HeaderVersion = headerVersion,
                OsVersion = osVersion,
                Name = name,
                Cmdline = cmdline,
                Id = id,
                RecoveryDtboSize = recoverySize,
                RecoveryDtboOffset = recoveryOff,
                BootHeaderSize = bootHeaderSize,
                DtbSize = dtbSize,
                DtbAddr = dtbAddr,
                Segments = segs,
                TotalSize = end,
            };
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Qualcomm-style appended DTB: v0/v1 kernels often concatenate
    /// gzip(Image) + DTB inside kernel_size (e.g. edk-fajita: 5453385 gzip +
    /// 557360 DTB). Returns the byte offset of the first DTB magic inside the
    /// kernel payload, or -1 when there is none.
    /// </summary>
    public static long FindAppendedDtb(byte[] kernelPayload)
    {
        try
        {
            for (int i = 0; i + DtbMinSize <= kernelPayload.Length; i++)
            {
                if (kernelPayload[i] != 0xD0 || kernelPayload[i + 1] != 0x0D
                    || kernelPayload[i + 2] != 0xFE || kernelPayload[i + 3] != 0xED)
                {
                    continue;
                }
                uint total = ReadU32BE(kernelPayload, i + 4);
                if (total < (uint)DtbMinSize || total > (uint)(kernelPayload.Length - i))
                {
                    continue;
                }
                if (total % 4 != 0)
                {
                    continue;
                }
                return i;
            }
            return -1;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    public static bool TryReadDtbSize(byte[] buf, int off, out uint totalSize)
    {
        totalSize = 0;
        try
        {
            if (off < 0 || off + DtbMinSize > buf.Length)
            {
                return false;
            }
            if (buf[off] != 0xD0 || buf[off + 1] != 0x0D || buf[off + 2] != 0xFE || buf[off + 3] != 0xED)
            {
                return false;
            }
            totalSize = ReadU32BE(buf, off + 4);
            if (totalSize < (uint)DtbMinSize || off + totalSize > buf.Length || totalSize % 4 != 0)
            {
                return false;
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
