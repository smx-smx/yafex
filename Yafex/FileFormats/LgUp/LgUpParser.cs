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
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Yafex.FileFormats.LgUp;

/// <summary>
/// Parser for LG "ELIX" .up/.epk packages. Ported from the elix lgup tool's
/// PackageParser, adapted to span-based reads. Only CInstallFull and
/// CInstallext4 sections are supported; anything else fails detection.
/// </summary>
public static class LgUpParser
{
    public static byte[] AesCbcDecrypt(byte[] data, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = LgUpFormat.Iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var dec = aes.CreateDecryptor();
        var outBuf = new byte[data.Length];
        var n = dec.TransformBlock(data, 0, data.Length, outBuf, 0);
        if (n != data.Length) throw new InvalidDataException("AES block misalignment");
        return outBuf;
    }

    public static byte[] StripPad(byte[] decrypted)
    {
        // CAes::DecryptInplace: if (last <= 0x10) size = len - last; else fail
        var last = decrypted[^1];
        if (last == 0 || last > 0x10 || last > decrypted.Length)
            throw new InvalidDataException($"invalid PKCS padding byte 0x{last:X2}");
        return decrypted[..^last];
    }

    private sealed class Cursor
    {
        private readonly byte[] _b;
        public int Pos;
        public int Length => _b.Length;
        public Cursor(byte[] b) { _b = b; }
        public uint U32()
        {
            var v = BitConverter.ToUInt32(_b, Pos);
            Pos += 4;
            return v;
        }
        public ulong U64()
        {
            var v = BitConverter.ToUInt64(_b, Pos);
            Pos += 8;
            return v;
        }
        public byte[] Bytes(int n)
        {
            var r = _b.AsSpan(Pos, n).ToArray();
            Pos += n;
            return r;
        }
        public string Ascii(int n)
        {
            var s = Encoding.ASCII.GetString(_b, Pos, n);
            Pos += n;
            return s;
        }
    }

    public static bool TryReadOuterMagic(byte[] file, out string magic)
    {
        magic = "";
        if (file.Length < 0x108) return false;
        magic = Encoding.ASCII.GetString(file, 0x104, 4);
        return magic == LgUpFormat.OuterMagicEpkg || magic == LgUpFormat.OuterMagicLgua;
    }

    /// <summary>
    /// Parses the package, trial-decrypting the header with each candidate key.
    /// Returns null when no key validates.
    /// </summary>
    public static (LgUpPackage Package, byte[] Key)? Parse(byte[] file, byte[][] keys)
    {
        if (!TryReadOuterMagic(file, out var outer)) return null;

        long packageStart = 0;
        byte[]? exec = null;
        uint lguaSize = 0, execSize = 0;
        if (outer == LgUpFormat.OuterMagicLgua)
        {
            lguaSize = BitConverter.ToUInt32(file, 0x100);
            execSize = BitConverter.ToUInt32(file, 0x108);
            packageStart = 0x100 + lguaSize;
            if (packageStart + 0x108 > file.Length) return null;
            exec = file.AsSpan(0x110, (int)execSize).ToArray();
            if (!TryReadOuterMagicAt(file, packageStart, out var inner) || inner != LgUpFormat.OuterMagicEpkg)
                return null;
        }

        foreach (var key in keys)
        {
            var pkg = TryParseWithKey(file, packageStart, key);
            if (pkg == null) continue;
            pkg.OuterMagic = outer;
            pkg.PackageStart = packageStart;
            pkg.LguaSize = lguaSize;
            pkg.ExecSize = execSize;
            pkg.Exec = exec ?? [];
            return (pkg, key);
        }
        return null;
    }

    private static bool TryReadOuterMagicAt(byte[] file, long start, out string magic)
    {
        magic = "";
        if (start + 0x108 > file.Length) return false;
        magic = Encoding.ASCII.GetString(file, (int)start + 0x104, 4);
        return magic == LgUpFormat.OuterMagicEpkg || magic == LgUpFormat.OuterMagicLgua;
    }

    private static LgUpPackage? TryParseWithKey(byte[] file, long start, byte[] key)
    {
        try
        {
            return ParseWithKey(file, start, key);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static LgUpPackage ParseWithKey(byte[] file, long start, byte[] key)
    {
        var pkg = new LgUpPackage();
        pkg.Signature = file.AsSpan((int)start, LgUpFormat.SignatureSize).ToArray();

        pkg.EncHeaderSize = BitConverter.ToUInt32(file, (int)start + 0x100);
        var magic = Encoding.ASCII.GetString(file, (int)start + 0x104, 4);
        if (magic != LgUpFormat.OuterMagicEpkg)
            throw new InvalidDataException($"unsupported package magic: {magic}");

        var encLen = checked((int)pkg.EncHeaderSize - 8);
        if (encLen <= 0 || start + 0x108 + encLen > file.Length)
            throw new InvalidDataException("encrypted header overruns file");
        pkg.EncryptedHeader = file.AsSpan((int)start + 0x108, encLen).ToArray();

        var dec = StripPad(AesCbcDecrypt(pkg.EncryptedHeader, key));
        pkg.DecryptedHeader = dec;
        pkg.HeaderSize = BitConverter.ToUInt32(dec, 0);
        if (pkg.HeaderSize != dec.Length)
            throw new InvalidDataException($"header size field 0x{pkg.HeaderSize:X} != actual 0x{dec.Length:X}");
        if (Encoding.ASCII.GetString(dec, 4, 4) != LgUpFormat.InnerMagic)
            throw new InvalidDataException("bad LGUP magic after decrypt");

        var cur = new Cursor(dec);
        cur.Pos = 8;
        var ver = cur.Ascii(4);
        pkg.Version = ver switch
        {
            "0500" => 5,
            "0400" => 4,
            _ => throw new InvalidDataException($"unsupported ELIX version {ver}"),
        };

        pkg.CompressType = cur.U32();
        if (pkg.Version == 5)
        {
            cur.Pos += 4; // reserved
            var tvLen = cur.U32();
            cur.Pos += checked((int)tvLen); // m_pTargetVersion (kept raw)
            cur.Pos += 4; // reserved
        }
        pkg.TotalDataSize = cur.U32();
        pkg.SectionCount = cur.U32();
        pkg.SectionHeaderSizes = new uint[pkg.SectionCount];
        for (var i = 0; i < pkg.SectionCount; i++) pkg.SectionHeaderSizes[i] = cur.U32();
        pkg.PatchOffsets = new uint[pkg.SectionCount];
        for (var i = 0; i < pkg.SectionCount; i++) pkg.PatchOffsets[i] = cur.U32();

        for (var i = 0; i < pkg.SectionCount; i++)
        {
            var size = checked((int)pkg.SectionHeaderSizes[i]);
            if (cur.Pos + size > dec.Length)
                throw new InvalidDataException("section header overruns decrypted header");
            var sh = cur.Bytes(size);
            pkg.Sections.Add(ParseSection(sh));
        }
        if (cur.Pos != dec.Length)
            throw new InvalidDataException($"section headers end at 0x{cur.Pos:X}, header is 0x{dec.Length:X} bytes");

        pkg.DataBase = start + LgUpFormat.SignatureSize + pkg.EncHeaderSize;
        if (pkg.DataBase + pkg.TotalDataSize > file.Length)
            throw new InvalidDataException("patch data region overruns file");
        pkg.TrailerOffset = pkg.DataBase + pkg.TotalDataSize;
        return pkg;
    }

    private static LgUpSection ParseSection(byte[] sh)
    {
        var sec = new LgUpSection { RawHeader = sh };
        var cur = new Cursor(sh);
        sec.InstallType = cur.U32();
        switch (sec.InstallType)
        {
            case LgUpFormat.InstallExt4: // CInstallext4::ParsePackage
            {
                var nl = cur.U32();
                sec.Name = Encoding.UTF8.GetString(cur.Bytes(checked((int)nl)));
                sec.SourceSize = cur.U64();
                sec.TargetSize = cur.U64();
                sec.KeyUnit = cur.U32();
                sec.TotalUnits = cur.U32();
                for (var u = 0; u < sec.TotalUnits; u++)
                    sec.Units.Add(ParseExt4Unit(cur));
                break;
            }
            case LgUpFormat.InstallFull: // CInstallFull::ParsePackage
            {
                var n1 = cur.U32();
                sec.Name = Encoding.UTF8.GetString(cur.Bytes(checked((int)n1)));
                var n2 = cur.U32();
                sec.Name2 = Encoding.UTF8.GetString(cur.Bytes(checked((int)n2)));
                sec.FullSize = cur.U64();
                sec.FullFlag = cur.U32();
                sec.FullCount = cur.U32();
                for (var u = 0; u < sec.FullCount; u++)
                    sec.Units.Add(ParseFullUnit(cur));
                break;
            }
            default:
                throw new InvalidDataException(
                    $"unsupported install type {LgUpFormat.InstallTypeName(sec.InstallType)}");
        }
        return sec;
    }

    // CInstallext4::_ParseUnitHeader
    private static LgUpUnit ParseExt4Unit(Cursor cur)
    {
        var u = new LgUpUnit();
        u.UpdateType = cur.U32();
        for (var ri = 0; ri < 5; ri++)
        {
            var count = cur.U32();
            for (var i = 0; i < count; i++)
                u.Rangesets[ri].Ranges.Add((cur.U64(), cur.U64()));
        }
        u.SourceMd5 = cur.Bytes(16);
        u.TargetMd5 = cur.Bytes(16);
        u.BackupMd5 = cur.Bytes(16);
        u.PatchSize = cur.U32();
        u.UncompressedSize = cur.U32();
        u.Sha256 = cur.Bytes(32);
        return u;
    }

    // CInstallFull::_ParseUnitHeader
    private static LgUpUnit ParseFullUnit(Cursor cur)
    {
        var u = new LgUpUnit { UpdateType = LgUpFormat.UnitNew };
        u.FullUnitSize = cur.U32();
        u.TargetMd5 = cur.Bytes(16);
        u.PatchSize = cur.U32();
        u.UncompressedSize = cur.U32();
        u.Sha256 = cur.Bytes(32);
        return u;
    }
}
