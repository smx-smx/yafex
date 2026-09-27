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

public sealed record Msd11TocItem(uint ItemId, string Name, bool Encrypted, byte[]? Salt);

public sealed record Msd11ImageInfo(string Name, ushort Major, ushort Minor);

/// <summary>OUITH (Tizen upgrade-tree) v1.9 TLV walk: upgrade items carry the
/// member name (0x0A) and optional AES salt (0x0E); groups carry the firmware
/// identity (0x19). Unknown sub-descriptors are skipped by size; unknown
/// top-level tags fail the parse.</summary>
public static class Msd11Toc
{
    public const string TreeMagic = "Tizen Software Upgrade Tree Binary Format ver. 1.9";

    private const uint TagItem = 0x01;
    private const uint TagGroup = 0x02;
    private const uint TagProcessing = 0x07;
    private const uint TagPartitionName = 0x0A;
    private const uint TagAes = 0x0E;
    private const uint TagRsa = 0x10;
    private const uint TagCrc32 = 0x12;
    private const uint TagGroupInfo = 0x13;
    private const uint TagImageVersion = 0x19;
    private const uint TagMetadata = 0x23;
    private const uint TagDowngrade = 0x37;
    private const uint TagBaseVersion = 0x3A;
    private const uint TagDeviceType = 0x3D;

    public static bool TryParse(
        byte[] blob, out List<Msd11TocItem> items, out Msd11ImageInfo? info)
    {
        items = new List<Msd11TocItem>();
        info = null;
        try
        {
            var r = new TocReader(blob);
            r.Skip(4); // public key id
            int sigSize = r.U16();
            r.Skip(sigSize);
            string magic = r.Ascii(51);
            if (magic != TreeMagic + "\0") return false;
            uint topCount = r.U32();
            if (topCount > 100000) return false;
            for (uint i = 0; i < topCount; i++)
            {
                r.Header(out uint size, out uint tag);
                if (tag == TagItem) ParseItem(r, items);
                else if (tag == TagGroup) info = ParseGroup(r, size, info);
                else if (tag == TagDowngrade) r.Skip((int)size - 4); // flag + 3*u32 after tag
                else if (tag == TagBaseVersion) r.Skip((int)size - 4); // flag + 2*u16 after tag
                else if (tag == TagMetadata) ParseMetadata(r);
                else return false;
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void ParseItem(TocReader r, List<Msd11TocItem> items)
    {
        r.Skip(1); // flag
        uint itemId = r.U32();
        r.Skip(1 + 8 + 8 + 2); // unk, original size, processed size, unk
        uint subCount = r.U32();
        if (subCount > 100000) throw new InvalidOperationException("bad subdescriptor count");
        string? name = null;
        bool encrypted = false;
        byte[]? salt = null;
        for (uint i = 0; i < subCount; i++)
        {
            long body = r.Header(out uint size, out uint tag);
            if (tag == TagPartitionName)
            {
                r.Skip(1); // flag
                name = r.Ascii(r.U8());
            }
            else if (tag == TagProcessing)
            {
                r.Skip(1); // flag
                uint procCount = r.U32();
                if (procCount > 100000) throw new InvalidOperationException("bad processing count");
                for (uint j = 0; j < procCount; j++)
                {
                    long pbody = r.Header(out uint psize, out uint ptag);
                    if (ptag == TagAes)
                    {
                        r.Skip(1); // flag
                        salt = r.Bytes(r.U8());
                        r.Skip(8); // processed size
                        encrypted = true;
                    }
                    else if (ptag == TagCrc32) r.Skip(1 + 4);
                    else if (ptag == TagRsa) { r.Skip(1 + 4); r.Skip(r.U16()); }
                    else r.Skip((int)psize - 4);
                    r.SeekEnd(pbody, psize);
                }
            }
            else if (tag == TagGroupInfo) r.Skip(1 + 4);
            else r.Skip((int)size - 4);
            r.SeekEnd(body, size);
        }
        if (name == null) throw new InvalidOperationException($"item {itemId} has no name");
        items.Add(new Msd11TocItem(itemId, name, encrypted, salt));
    }

    private static Msd11ImageInfo? ParseGroup(TocReader r, uint size, Msd11ImageInfo? current)
    {
        r.Skip(1 + 4); // flag + group id
        uint subCount = r.U32();
        if (subCount > 100000) throw new InvalidOperationException("bad group subdescriptor count");
        for (uint i = 0; i < subCount; i++)
        {
            long body = r.Header(out uint ssize, out uint stag);
            if (stag == TagImageVersion)
            {
                r.Skip(1); // flag
                string name = r.Ascii(r.U8());
                // Newer blobs carry one pad byte before the version pair.
                if ((long)ssize - name.Length - 4 == 7) r.Skip(1);
                ushort major = r.U16();
                ushort minor = r.U16();
                current = new Msd11ImageInfo(name, major, minor);
            }
            else if (stag == TagDeviceType) { r.Skip(1); r.Skip(r.U8()); }
            else r.Skip((int)ssize - 4);
            r.SeekEnd(body, ssize);
        }
        return current;
    }

    private static void ParseMetadata(TocReader r)
    {
        r.Skip(1); // flag
        r.Skip(r.U8()); // name
        r.Skip(r.U8()); // value
    }

    private sealed class TocReader
    {
        private readonly byte[] _buf;
        private int _pos;

        public TocReader(byte[] buf) { _buf = buf; }

        // Descriptor header is flag(1) + size(4) + tag(4); size counts the
        // tag, so the body after it spans size-4 bytes.
        public long Header(out uint size, out uint tag)
        {
            Skip(1);
            size = U32();
            tag = U32();
            if (size < 4) throw new InvalidOperationException("bad descriptor size");
            return _pos;
        }

        public void SeekEnd(long bodyStart, uint size)
        {
            long end = bodyStart + (long)size - 4;
            if (end < _pos || end > _buf.Length) throw new InvalidOperationException("descriptor overrun");
            _pos = (int)end;
        }

        public void Skip(int n)
        {
            if (n < 0 || _pos + n > _buf.Length) throw new InvalidOperationException("blob overrun");
            _pos += n;
        }

        public byte U8() { Skip(1); return _buf[_pos - 1]; }

        public ushort U16()
        {
            Skip(2);
            return BitConverter.ToUInt16(_buf, _pos - 2);
        }

        public uint U32()
        {
            Skip(4);
            return BitConverter.ToUInt32(_buf, _pos - 4);
        }

        public byte[] Bytes(int n) { Skip(n); var b = new byte[n]; Array.Copy(_buf, _pos - n, b, 0, n); return b; }

        public string Ascii(int n) { return Encoding.ASCII.GetString(Bytes(n)); }
    }
}
