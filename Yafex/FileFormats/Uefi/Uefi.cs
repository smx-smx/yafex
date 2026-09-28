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
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Yafex.FileFormats.Uefi;

/// <summary>
/// Intel/AMD UEFI firmware image parser: flash descriptor regions, firmware
/// volumes, FFS file chains, sections (with EFI/Tiano decompression), AMI
/// NVAR stores and the FIT/microcode table. All readers are bounds-checked
/// and fail closed; chain walks resync (bounded) instead of dying on the
/// first hostile byte.
/// </summary>
public static class Uefi
{
    public sealed record FlashRegion(string Name, int Index, long Offset, long Length);
    public sealed record FirmwareVolume(long Offset, long Length, Guid FsGuid, long FilesOffset);
    public sealed record FfsFile(long Offset, Guid Name, byte Type, byte Attributes,
        long Size, int HeaderSize, long BodyOffset, int State);
    public sealed record Section(long Offset, byte Type, int Size, long PayloadOffset, int PayloadSize);
    public sealed record CompressInfo(int UncompressedLength, byte CompressionType, byte[] Stream);
    public sealed record GuidedInfo(Guid DefinitionGuid, int DataOffset, ushort Attributes);
    public sealed record NvarVariable(string? Name, byte[]? NameGuid, byte Attributes,
        long DataOffset, int DataSize);
    public sealed record FitEntry(long Address, long FileOffset, int Size, byte Version, byte Type);

    private static readonly byte[] Erased16 = new byte[16]
    {
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    };

    private static int ReadU16(byte[] b, long o) => b[o] | (b[o + 1] << 8);
    private static int ReadU24(byte[] b, long o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16);
    private static int ReadU32(byte[] b, long o) =>
        b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
    private static long ReadU64(byte[] b, long o) =>
        (long)((ulong)b[o] | ((ulong)b[o + 1] << 8) | ((ulong)b[o + 2] << 16) | ((ulong)b[o + 3] << 24)
        | ((ulong)b[o + 4] << 32) | ((ulong)b[o + 5] << 40) | ((ulong)b[o + 6] << 48) | ((ulong)b[o + 7] << 56));

    private static bool IsErased(byte[] b, long o, int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (b[o + i] != 0xFF)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsZero(byte[] b, long o, int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (b[o + i] != 0)
            {
                return false;
            }
        }
        return true;
    }

    private static Guid ReadGuid(byte[] b, long o)
    {
        var tmp = new byte[16];
        Buffer.BlockCopy(b, (int)o, tmp, 0, 16);
        return new Guid(tmp);
    }

    private static bool HasBytes(byte[] b, long o, long n) => o >= 0 && n >= 0 && o + n <= b.Length;

    // ---- flash descriptor ----

    public static bool HasFlashDescriptor(byte[] img)
    {
        return img.Length >= 0x20 && ReadU32(img, UefiStructures.FlashDescriptorSigOffset)
            == (int)UefiStructures.FlashDescriptorSignature;
    }

    /// <summary>Short-prefix variant for mount sniffing (32-byte window).</summary>
    public static bool HasFlashDescriptorPrefix(byte[] window)
    {
        return window.Length >= 0x20 && ReadU32(window, UefiStructures.FlashDescriptorSigOffset)
            == (int)UefiStructures.FlashDescriptorSignature;
    }

    /// <summary>
    /// Intel flash descriptor region split (base/limit in 4KB units).
    /// Empty when no (valid) descriptor is present. The signature word
    /// alone is not trusted (it collides with FIT-area data on
    /// BIOS-region-only ROMs): a descriptor must map at least the
    /// descriptor and BIOS regions, start region 0 at zero, and not
    /// overlap itself.
    /// </summary>
    public static List<FlashRegion> ReadRegions(byte[] img)
    {
        var regions = new List<FlashRegion>();
        if (!HasFlashDescriptor(img))
        {
            return regions;
        }
        try
        {
            int flMap0 = ReadU32(img, 0x14);
            int frba = ((flMap0 >> 16) & 0xFF) * 16;
            int nr = ((flMap0 >> 24) & 0xFF) + 1;
            if (!HasBytes(img, frba, (long)nr * 4))
            {
                return regions;
            }
            var found = new List<FlashRegion>();
            for (int i = 0; i < nr && i < 16; i++)
            {
                int flReg = ReadU32(img, frba + i * 4);
                int base4k = flReg & 0x7FFF;
                int limit4k = (flReg >> 16) & 0x7FFF;
                if (limit4k < base4k)
                {
                    continue;
                }
                long off = (long)base4k * 0x1000;
                long len = ((long)limit4k - base4k + 1) * 0x1000;
                if (off < 0 || off + len > img.Length || len <= 0)
                {
                    continue;
                }
                found.Add(new FlashRegion(UefiStructures.RegionName(i), i, off, len));
            }
            if (found.Count < 2 || found.All(r => r.Index != 0 || r.Offset != 0))
            {
                return regions;
            }
            var ordered = found.OrderBy(r => r.Offset).ToList();
            for (int i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].Offset < ordered[i - 1].Offset + ordered[i - 1].Length)
                {
                    return regions;
                }
            }
            regions.AddRange(found);
        }
        catch (IndexOutOfRangeException)
        {
            return new List<FlashRegion>();
        }
        return regions;
    }

    // ---- firmware volumes ----

    public static bool TryGetVolumeAt(byte[] img, long offset, out FirmwareVolume volume)
    {
        volume = null!;
        if (!HasBytes(img, offset, 56))
        {
            return false;
        }
        if (img[offset + 40] != '_' || img[offset + 41] != 'F'
            || img[offset + 42] != 'V' || img[offset + 43] != 'H')
        {
            return false;
        }
        long length = ReadU64(img, offset + 32);
        int headerLength = ReadU16(img, offset + 48);
        if (length <= 0 || offset + length > img.Length || length > 1L << 32)
        {
            return false;
        }
        if (headerLength < 56 || headerLength > 0x1000 || (headerLength & 1) != 0)
        {
            return false;
        }
        if (!HasBytes(img, offset, headerLength))
        {
            return false;
        }
        int sum = 0;
        for (int i = 0; i < headerLength; i += 2)
        {
            sum += ReadU16(img, offset + i);
        }
        if ((sum & 0xFFFF) != 0)
        {
            return false;
        }
        long files = offset + headerLength;
        int ext = ReadU16(img, offset + 52);
        if (ext != 0)
        {
            if (!HasBytes(img, offset + ext, 20))
            {
                return false;
            }
            int extSize = ReadU32(img, offset + ext + 16);
            if (extSize < 20 || !HasBytes(img, offset + ext, extSize))
            {
                return false;
            }
            long afterExt = offset + ext + extSize;
            if (afterExt > files)
            {
                files = (afterExt + 7) & ~7L;
            }
        }
        if (files > offset + length)
        {
            return false;
        }
        volume = new FirmwareVolume(offset, length, ReadGuid(img, offset + 16), files);
        return true;
    }

    /// <summary>All checksum-valid volumes fully inside the image.</summary>
    public static List<FirmwareVolume> FindVolumes(byte[] img)
    {
        var volumes = new List<FirmwareVolume>();
        for (long off = 0; off + 56 <= img.Length; off++)
        {
            if (img[off + 40] == '_' && img[off + 41] == 'F'
                && img[off + 42] == 'V' && img[off + 43] == 'H'
                && TryGetVolumeAt(img, off, out var vol))
            {
                volumes.Add(vol);
                off = vol.Offset + vol.Length - 1;
            }
        }
        return volumes;
    }

    // ---- FFS files ----

    private static bool PlausibleFileHeader(byte[] img, long pos, long end, out FfsFile? file)
    {
        file = null;
        if (!HasBytes(img, pos, 24) || pos + 24 > end)
        {
            return false;
        }
        var name = ReadGuid(img, pos);
        byte type = img[pos + 18];
        byte attr = img[pos + 19];
        int size = ReadU24(img, pos + 20);
        int state = img[pos + 23];
        int headerSize = 24;
        long total = size;
        if (size == 0xFFFFFF)
        {
            if (!HasBytes(img, pos + 24, 8) || pos + 32 > end)
            {
                return false;
            }
            total = ReadU64(img, pos + 24);
            headerSize = 32;
        }
        if (!UefiStructures.FileTypes.ContainsKey(type) || total < headerSize
            || pos + total > end || total > 1L << 28)
        {
            return false;
        }
        // 8-bit header checksum over the header (checksum fields zeroed,
        // state excluded); enforced on real images 899/899.
        int sum = 0;
        int hlen = size == 0xFFFFFF ? 24 : (int)Math.Min(total, 24);
        for (int i = 0; i < hlen; i++)
        {
            if (i == 16 || i == 17 || i == 23)
            {
                continue;
            }
            sum += img[pos + i];
        }
        if (img[pos + 16] != ((256 - (sum & 0xFF)) & 0xFF))
        {
            return false;
        }
        file = new FfsFile(pos, name, type, attr, total, headerSize, pos + headerSize, state);
        return true;
    }

    /// <summary>
    /// FFS file chain of an FFS-filesystem volume (empty for NVRAM/unknown
    /// filesystems). Erased runs are skipped; bounded resync on corruption.
    /// </summary>
    public static List<FfsFile> ReadFiles(byte[] img, FirmwareVolume vol, int cap = 100000)
    {
        var files = new List<FfsFile>();
        if (!UefiStructures.IsFfsFileSystem(vol.FsGuid))
        {
            return files;
        }
        long end = vol.Offset + vol.Length;
        long pos = vol.FilesOffset;
        int errors = 0;
        while (pos + 24 <= end && files.Count < cap)
        {
            if (IsErased(img, pos, 16) || IsZero(img, pos, 16))
            {
                long next = pos;
                while (next + 8 <= end && (IsErased(img, next, 8) || IsZero(img, next, 8)))
                {
                    next += 8;
                }
                pos = next == pos ? pos + 8 : next;
                continue;
            }
            if (PlausibleFileHeader(img, pos, end, out var file) && file != null)
            {
                files.Add(file);
                pos = (file.Offset + file.Size + 7) & ~7L;
                errors = 0;
            }
            else
            {
                pos += 8;
                if (++errors > 64)
                {
                    break;
                }
            }
        }
        return files;
    }

    // ---- sections ----

    /// <summary>Section list of one file body (stops at padding/corruption).</summary>
    public static List<Section> ReadSections(byte[] img, FfsFile file, int cap = 10000)
    {
        var sections = new List<Section>();
        long bodyEnd = file.Offset + file.Size;
        long pos = file.BodyOffset;
        while (pos + 4 <= bodyEnd && sections.Count < cap)
        {
            if (!HasBytes(img, pos, 4))
            {
                break;
            }
            if (img[pos] == 0xFF && img[pos + 1] == 0xFF && img[pos + 2] == 0xFF && img[pos + 3] == 0xFF)
            {
                break;
            }
            int size = ReadU24(img, pos);
            byte type = img[pos + 3];
            if (size < 4 || pos + size > bodyEnd)
            {
                break;
            }
            sections.Add(new Section(pos, type, size, pos + 4, size - 4));
            pos += (size + 3) & ~3;
        }
        return sections;
    }

    public static byte[] SectionBytes(byte[] img, Section s)
    {
        var out_ = new byte[s.PayloadSize];
        Buffer.BlockCopy(img, (int)s.PayloadOffset, out_, 0, s.PayloadSize);
        return out_;
    }

    public static string? ReadUiName(byte[] img, Section s)
    {
        if (s.PayloadSize < 2)
        {
            return null;
        }
        try
        {
            string text = Encoding.Unicode.GetString(img, (int)s.PayloadOffset, s.PayloadSize)
                .TrimEnd('\0');
            return text.Length == 0 ? null : text;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static bool TryReadCompress(byte[] img, Section s, out CompressInfo info)
    {
        info = null!;
        if (s.Type != 0x01 || s.PayloadSize < 5)
        {
            return false;
        }
        int uncompressed = ReadU32(img, s.PayloadOffset);
        byte ctype = img[s.PayloadOffset + 4];
        if (uncompressed < 0 || uncompressed > 1 << 30)
        {
            return false;
        }
        var stream = new byte[s.PayloadSize - 5];
        Buffer.BlockCopy(img, (int)s.PayloadOffset + 5, stream, 0, stream.Length);
        info = new CompressInfo(uncompressed, ctype, stream);
        return true;
    }

    /// <summary>
    /// Decompresses a compression-section stream. Type 1 names the EFI and
    /// Tiano variants with one byte, so EFI is tried before Tiano and the
    /// stream's own OrigSize picks the winner.
    /// </summary>
    public static byte[] DecompressSection(CompressInfo info)
    {
        if (info.CompressionType == 0)
        {
            return info.Stream;
        }
        if (info.CompressionType != 1)
        {
            throw new InvalidDataException(
                $"unsupported compression type {info.CompressionType}");
        }
        return TianoDecompressor.DecompressSection(info.Stream, info.UncompressedLength);
    }

    public static bool TryReadGuided(byte[] img, Section s, out GuidedInfo info)
    {
        info = null!;
        if (s.Type != 0x02 || s.PayloadSize < 20)
        {
            return false;
        }
        var guid = ReadGuid(img, s.PayloadOffset);
        int dataOff = ReadU16(img, s.PayloadOffset + 16);
        ushort attr = (ushort)ReadU16(img, s.PayloadOffset + 18);
        if (dataOff < 4 + 20 || dataOff > s.Size)
        {
            return false;
        }
        info = new GuidedInfo(guid, dataOff, attr);
        return true;
    }

    /// <summary>
    /// Decompresses a GUID-defined section by its definition GUID (LZMA,
    /// Tiano, AMD zlib, Qualcomm gzip). Anything else stays raw (caller
    /// emits it unprocessed with the GUID noted).
    /// </summary>
    public static byte[] DecompressGuided(byte[] img, Section s, GuidedInfo info)
    {
        int absData = (int)(s.Offset + info.DataOffset);
        int len = (int)(s.Offset + s.Size) - absData;
        if (len <= 8)
        {
            throw new InvalidDataException("truncated guided section");
        }
        if (info.DefinitionGuid == UefiStructures.GuidedTiano)
        {
            var stream = new byte[len];
            Buffer.BlockCopy(img, absData, stream, 0, len);
            InvalidDataException? first = null;
            foreach (int bits in new[] { TianoDecompressor.EfiPositionBits, TianoDecompressor.TianoPositionBits })
            {
                try
                {
                    return TianoDecompressor.Decompress(stream, bits);
                }
                catch (InvalidDataException ex)
                {
                    first ??= ex;
                }
            }
            throw new InvalidDataException("guided Tiano section did not decode: " + first?.Message);
        }
        if (info.DefinitionGuid == UefiStructures.GuidedZlibAmd)
        {
            // 0x100-byte AMD header, then a raw zlib stream.
            if (len < 0x100 + 8)
            {
                throw new InvalidDataException("truncated AMD zlib section");
            }
            using var raw = new MemoryStream(img, absData + 0x100, len - 0x100, writable: false);
            using var zs = new ZLibStream(raw, CompressionMode.Decompress);
            using var out_ = new MemoryStream();
            zs.CopyTo(out_);
            return out_.ToArray();
        }
        if (info.DefinitionGuid == UefiStructures.GuidedGzipQc)
        {
            using var raw = new MemoryStream(img, absData, len, writable: false);
            using var gz = new GZipStream(raw, CompressionMode.Decompress);
            using var out_ = new MemoryStream();
            gz.CopyTo(out_);
            return out_.ToArray();
        }
        if (info.DefinitionGuid == UefiStructures.GuidedLzma
            || info.DefinitionGuid == UefiStructures.GuidedLzmaHp)
        {
            var stream = new byte[len];
            Buffer.BlockCopy(img, absData, stream, 0, len);
            return LzmaDecoder.Decompress(stream);
        }
        throw new InvalidDataException(
            $"unsupported guided section {info.DefinitionGuid}");
    }

    /// <summary>
    /// Firmware volumes with non-empty file chains inside a decompressed
    /// blob. Distinguishes real nested images (emit whole + recurse) from
    /// bare section streams (walk sections); the file-chain requirement
    /// keeps phantom _FVH byte patterns from misfiring.
    /// </summary>
    public static List<FirmwareVolume> InnerFirmwareVolumes(byte[] inner)
    {
        var found = new List<FirmwareVolume>();
        foreach (var v in FindVolumes(inner))
        {
            if (ReadFiles(inner, v, cap: 4).Count > 0)
            {
                found.Add(v);
            }
            if (found.Count >= 4)
            {
                break;
            }
        }
        return found;
    }

    // ---- AMI NVAR store ----

    private const byte NvarGuidBit = 4;
    private const byte NvarDataBit = 8;
    private const byte NvarAsciiBit = 2;

    /// <summary>
    /// AMI NVAR variable walk (TotalSize stride, resync on corruption).
    /// Matches the ecosystem behavior (entries may overlap superseded data).
    /// </summary>
    public static List<NvarVariable> ReadNvarStore(byte[] buf, long offset, int length)
    {
        var vars = new List<NvarVariable>();
        long end = Math.Min(offset + length, buf.Length);
        long pos = offset;
        int errors = 0;
        while (pos + 10 <= end && vars.Count < 100000)
        {
            if (buf[pos] != 'N' || buf[pos + 1] != 'V' || buf[pos + 2] != 'A' || buf[pos + 3] != 'R')
            {
                long next = pos + 1;
                while (next + 4 <= end && (buf[next] != 'N' || buf[next + 1] != 'V'
                    || buf[next + 2] != 'A' || buf[next + 3] != 'R'))
                {
                    next++;
                }
                pos = next;
                if (++errors > 16)
                {
                    break;
                }
                continue;
            }
            int total = ReadU16(buf, pos + 4);
            byte attr = buf[pos + 9];
            if (total < 10 || pos + total > end)
            {
                pos += 4;
                if (++errors > 16)
                {
                    break;
                }
                continue;
            }
            long p = pos + 10;
            byte[]? guid = null;
            if ((attr & NvarGuidBit) != 0)
            {
                if (p + 16 > pos + total)
                {
                    pos += 4;
                    continue;
                }
                guid = new byte[16];
                Buffer.BlockCopy(buf, (int)p, guid, 0, 16);
                p += 16;
            }
            else
            {
                p += 1;
            }
            string? name = null;
            if ((attr & NvarDataBit) == 0)
            {
                bool ascii = (attr & NvarAsciiBit) != 0;
                if (ascii)
                {
                    long e = p;
                    while (e < pos + total && buf[e] != 0)
                    {
                        e++;
                    }
                    if (e >= pos + total)
                    {
                        pos += total;
                        continue;
                    }
                    name = Encoding.ASCII.GetString(buf, (int)p, (int)(e - p));
                    p = e + 1;
                }
                else
                {
                    long e = p;
                    while (e + 1 < pos + total && (buf[e] != 0 || buf[e + 1] != 0))
                    {
                        e += 2;
                    }
                    if (e + 1 >= pos + total)
                    {
                        pos += total;
                        continue;
                    }
                    try
                    {
                        name = Encoding.Unicode.GetString(buf, (int)p, (int)(e - p));
                    }
                    catch (ArgumentException)
                    {
                        name = null;
                    }
                    p = e + 2;
                }
            }
            long dataEnd = pos + total;
            vars.Add(new NvarVariable(name, guid, attr, p, (int)Math.Max(0, dataEnd - p)));
            pos += total;
            errors = 0;
        }
        return vars;
    }

    // ---- FIT / microcode ----

    /// <summary>
    /// Firmware Interface Table at the top of the image (empty when absent).
    /// Entries carry the microcode-update addresses; checksums are reported
    /// but not enforced (vendors ship them zeroed).
    /// </summary>
    public static List<FitEntry> ReadFit(byte[] img)
    {
        var entries = new List<FitEntry>();
        if (img.Length < 0x40)
        {
            return entries;
        }
        long ptr = (uint)ReadU32(img, img.Length - 0x40);
        long off = img.Length - (0x1_0000_0000L - ptr);
        if (ptr < 0xFF000000L || !HasBytes(img, off, 16))
        {
            return entries;
        }
        if (img[off] != '_' || img[off + 1] != 'F' || img[off + 2] != 'I'
            || img[off + 3] != 'T' || img[off + 4] != '_'
            || img[off + 5] != 0x20 || img[off + 6] != 0x20 || img[off + 7] != 0x20)
        {
            return entries;
        }
        int count = ReadU32(img, off + 8);
        if (count <= 0 || count > 256 || !HasBytes(img, off, count * 16L))
        {
            return entries;
        }
        for (int i = 0; i < count; i++)
        {
            long e = off + i * 16;
            long addr = ReadU64(img, e);
            if (addr == -1)
            {
                break;
            }
            int size = ReadU32(img, e + 8);
            byte version = img[e + 12];
            byte type = img[e + 14];
            long fileOff = addr >= 0xFF000000L ? img.Length - (0x1_0000_0000L - addr) : -1;
            entries.Add(new FitEntry(addr, fileOff, size, version, type));
        }
        return entries;
    }

    /// <summary>Microcode update at an FIT entry (magic + sane sizes).</summary>
    public static bool TryReadMicrocode(byte[] img, FitEntry entry, out byte[] update)
    {
        update = null!;
        if (entry.FileOffset < 0 || !HasBytes(img, entry.FileOffset, 48))
        {
            return false;
        }
        long o = entry.FileOffset;
        if (ReadU32(img, o) != 1)
        {
            return false;
        }
        int total = ReadU32(img, o + 32);
        if (total <= 0 || total > 1 << 22 || !HasBytes(img, o, total))
        {
            return false;
        }
        update = new byte[total];
        Buffer.BlockCopy(img, (int)o, update, 0, total);
        return true;
    }
}
