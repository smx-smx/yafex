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

using Smx.SharpIO.Memory.Buffers;

using Yafex.FileFormats.Lzhs;

namespace Yafex.FileFormats.MtkBoot;

/// <summary>
/// Mediatek boot images (MTK/DTV/ROMCODE magic at 0x100): a first-stage
/// loader prefix plus LZHS-compressed segments at fixed offsets (mtkloader,
/// uboot, boot_tz). Layout mirrors the reference splitter.
/// </summary>
public static class MtkBoot
{
    public const string PblMagic = "MTK/DTV/ROMCODE/MSDCBOOT";
    public const string RomMagic = "MTK/DTV/ROMCODE/NANDBOOT";
    public const int MagicOffset = 0x100;
    public const int PblSize = 0x9FFF;
    public const int RomSize = 0x4000;

    public const int LoaderOff = 0xA040;
    public const int LoaderOff1 = 0x4040;
    public const int UbootOff = 0x80000;
    public const int HisenseUbootOff = 0xA0000;
    public const int TzRelOff = 0x200;

    public sealed class Segment
    {
        public string Name = "";
        public int Offset;
        public byte[] Decoded = Array.Empty<byte>();
    }

    private static bool MagicAt(byte[] image, string magic)
    {
        if (image.Length < MagicOffset + magic.Length)
        {
            return false;
        }
        for (int i = 0; i < magic.Length; i++)
        {
            if (image[MagicOffset + i] != (byte)magic[i])
            {
                return false;
            }
        }
        return true;
    }

    public static bool IsPbl(byte[] image) => MagicAt(image, PblMagic) && image.Length >= PblSize;
    public static bool IsRom(byte[] image) => MagicAt(image, RomMagic) && image.Length >= RomSize;
    public static bool HasMagic(byte[] image) => IsPbl(image) || IsRom(image);

    public static int FirstStageSize(byte[] image) => IsPbl(image) ? PblSize : RomSize;

    private static bool TryDecodeSegment(byte[] image, int offset, out byte[] decoded)
    {
        decoded = Array.Empty<byte>();
        try
        {
            if (offset < 0 || offset + LzhsHeader.SIZE > image.Length)
            {
                return false;
            }
            ReadOnlyMemory64<byte> whole = image;
            var mem = whole.Slice(offset);
            var dec = new LzhsDecoder(mem);
            if ((long)dec.header.CompressedSize <= 0
                || LzhsHeader.SIZE + (long)dec.header.CompressedSize > mem.Length)
            {
                return false;
            }
            decoded = dec.DecodeToArray();
            return dec.VerifyChecksum()
                && decoded.Length == dec.header.UncompressedSize;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Carve + decode the LZHS segments present in the image.</summary>
    public static List<Segment> Split(byte[] image)
    {
        var found = new List<Segment>();
        foreach (int off in new[] { LoaderOff, LoaderOff1 })
        {
            if (TryDecodeSegment(image, off, out var decoded))
            {
                found.Add(new Segment { Name = "mtkloader", Offset = off, Decoded = decoded });
                break;
            }
        }
        foreach (var (off, name) in new[] { (UbootOff, "uboot"), (HisenseUbootOff, "uboot") })
        {
            if (TryDecodeSegment(image, off, out var decoded))
            {
                found.Add(new Segment { Name = name, Offset = off, Decoded = decoded });
                // TZ follows the uboot segment, 16-aligned + 0x200
                ReadOnlyMemory64<byte> whole = image;
                var mem = whole.Slice(off);
                var hdr = new LzhsDecoder(mem).header;
                long pad = hdr.CompressedSize % 16 == 0 ? 0 : 16 - hdr.CompressedSize % 16;
                long tz = (long)off + LzhsHeader.SIZE + hdr.CompressedSize + pad + TzRelOff;
                if (tz < image.Length && TryDecodeSegment(image, (int)tz, out var tzDecoded))
                {
                    found.Add(new Segment { Name = "boot_tz", Offset = (int)tz, Decoded = tzDecoded });
                }
                break;
            }
        }
        return found;
    }
}
