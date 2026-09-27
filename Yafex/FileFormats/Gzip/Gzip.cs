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
using System.IO.Compression;
using System.Text;

using Yafex.Support;

namespace Yafex.FileFormats.Gzip;

/// <summary>
/// Single-member gzip streams (these firmwares). Detection fully validates:
/// header, deflate payload, CRC-32 and ISIZE trailer, exact end-of-input.
/// </summary>
public static class Gzip
{
    public sealed class Member
    {
        public string FileName = "";
        public uint MTime;
        public byte Os;
        public byte[] Payload = Array.Empty<byte>();
    }

    // Cheap head sniff shared with the mount expander.
    public static bool HasHeader(byte[] prefix)
    {
        return prefix.Length >= 3 && prefix[0] == 0x1F && prefix[1] == 0x8B && prefix[2] == 0x08;
    }

    public static bool TryReadMember(byte[] image, out Member member)
    {
        member = new Member();
        try
        {
            if (!HasHeader(image) || image.Length < 18)
            {
                return false;
            }
            byte flg = image[3];
            if ((flg & 0xE0) != 0)
            {
                return false;
            }
            member.MTime = (uint)(image[4] | (image[5] << 8) | (image[6] << 16) | (image[7] << 24));
            member.Os = image[9];
            int off = 10;
            if ((flg & 0x04) != 0)
            {
                if (off + 2 > image.Length) return false;
                int xlen = image[off] | (image[off + 1] << 8);
                off += 2 + xlen;
            }
            string fname = "";
            if ((flg & 0x08) != 0)
            {
                int end = off;
                while (end < image.Length && image[end] != 0) end++;
                if (end >= image.Length || end - off > 256) return false;
                fname = Encoding.ASCII.GetString(image, off, end - off);
                off = end + 1;
            }
            if ((flg & 0x10) != 0)
            {
                int end = off;
                while (end < image.Length && image[end] != 0) end++;
                if (end >= image.Length) return false;
                off = end + 1;
            }
            if ((flg & 0x02) != 0)
            {
                off += 2;
            }
            if (off + 8 > image.Length)
            {
                return false;
            }
            // single member must run to end of input (8-byte trailer last)
            int deflateLen = image.Length - off - 8;
            if (deflateLen <= 0)
            {
                return false;
            }
            byte[] payload;
            using (var raw = new MemoryStream(image, off, deflateLen, writable: false))
            using (var inf = new DeflateStream(raw, CompressionMode.Decompress))
            using (var plain = new MemoryStream())
            {
                // DeflateStream may buffer ahead, so end-of-stream (not the raw
                // position) plus the CRC/ISIZE trailer below decide validity.
                inf.CopyTo(plain);
                payload = plain.ToArray();
            }
            uint wantCrc = (uint)(image[image.Length - 8] | (image[image.Length - 7] << 8)
                | (image[image.Length - 6] << 16) | (image[image.Length - 5] << 24));
            uint wantSize = (uint)(image[image.Length - 4] | (image[image.Length - 3] << 8)
                | (image[image.Length - 2] << 16) | (image[image.Length - 1] << 24));
            if (Crc32.Compute(payload, 0, payload.Length) != wantCrc
                || (uint)payload.Length != wantSize)
            {
                return false;
            }
            member.FileName = fname;
            member.Payload = payload;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static byte[] Inflate(byte[] image)
    {
        if (!TryReadMember(image, out var member))
        {
            throw new InvalidDataException("not a gzip stream");
        }
        return member.Payload;
    }
}
