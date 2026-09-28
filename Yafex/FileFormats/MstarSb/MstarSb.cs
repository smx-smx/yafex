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
// MStar secure-boot structures and algorithms, ported with permission from
// smx-smx/sb_verify (Author: Smx). Rewritten in Yafex style (manual
// little-endian reads instead of unsafe fixed buffers); verification math
// (fragment stripes, RSA/SHA256) is faithful to the original.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Yafex.FileFormats.MstarSb;

/// <summary>SECURITY fragment header + boot key-slot layouts.</summary>
public static class MstarSb
{
    public const string SecurityMagic = "SECURITY";
    public const string KeyBankMagicId = "Mstar.Key.Bank..";
    public const int HeaderSize = 16;
    public const int FullVerifyRegion = 0x2110;
    public const int SignatureSize = 256;

    public sealed class SecurityHeader
    {
        public int Offset;
        public uint FragNum;
        public uint FragUnitSize;
    }

    public sealed class KeySlot
    {
        public int Offset;
        public string MagicId = "";
        public byte[] BootModulus = Array.Empty<byte>();
        public byte[] BootExponent = Array.Empty<byte>();
    }

    private static uint ReadU32LE(byte[] buf, int off)
    {
        return (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));
    }

    // Cheap head sniff shared with the mount expander.
    public static bool HasMagic(byte[] prefix)
    {
        if (prefix.Length < 8)
        {
            return false;
        }
        for (int i = 0; i < 8; i++)
        {
            if (prefix[i] != (byte)SecurityMagic[i])
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>16-aligned scan for SECURITY headers, like FindSecurityHeader.</summary>
    public static List<SecurityHeader> FindHeaders(byte[] image)
    {
        var found = new List<SecurityHeader>();
        for (int off = 0; off + HeaderSize <= image.Length; off += 16)
        {
            bool match = true;
            for (int i = 0; i < 8; i++)
            {
                if (image[off + i] != (byte)SecurityMagic[i])
                {
                    match = false;
                    break;
                }
            }
            if (!match)
            {
                continue;
            }
            uint fragNum = ReadU32LE(image, off + 8);
            uint fragUnit = ReadU32LE(image, off + 12);
            if (fragNum < 1 || fragNum > 1024 || fragUnit < 1 || fragUnit > (1 << 24))
            {
                continue;
            }
            found.Add(new SecurityHeader { Offset = off, FragNum = fragNum, FragUnitSize = fragUnit });
        }
        return found;
    }

    /// <summary>
    /// Boot key-slots: SECURITY headers whose MagicID validates, for RSA
    /// verification. Layout: magic[8] + info*2 + signature[256] + 3 RSA keys
    /// (N[256]+E[4]) + 2 AES keys + magicID[16] + crc.
    /// </summary>
    public static List<KeySlot> FindKeySlots(byte[] image)
    {
        var found = new List<KeySlot>();
        const int need = 8 + 8 + 256 + 3 * 260 + 32 + 16 + 4;
        foreach (var h in FindHeaders(image))
        {
            int off = h.Offset;
            if (off + need > image.Length)
            {
                continue;
            }
            int idOff = off + 8 + 8 + 256 + 3 * 260 + 32;
            string id = Encoding.ASCII.GetString(image, idOff, 16);
            if (id != KeyBankMagicId)
            {
                continue;
            }
            int keyBase = off + 8 + 8 + 256;
            var n = new byte[256];
            var e = new byte[4];
            Array.Copy(image, keyBase, n, 0, 256);
            Array.Copy(image, keyBase + 256, e, 0, 4);
            found.Add(new KeySlot { Offset = off, MagicId = id, BootModulus = n, BootExponent = e });
        }
        return found;
    }

    /// <summary>
    /// Reassemble one verification stripe (VerifyImagePartial math): every
    /// frag_num-th unit starting at frag_start_idx.
    /// </summary>
    public static byte[] ReassembleStripe(byte[] image, int dataLength, SecurityHeader hdr, int fragStartIdx, int units)
    {
        long skip = (long)hdr.FragNum * hdr.FragUnitSize;
        long start = (long)fragStartIdx * hdr.FragUnitSize;
        var output = new MemoryStream();
        for (int i = 0; i < units; i++)
        {
            long off = start + i * skip;
            int take = (int)Math.Min(hdr.FragUnitSize, Math.Max(0, Math.Min(dataLength, image.Length) - off));
            if (take <= 0 || off < 0 || off >= image.Length)
            {
                break;
            }
            output.Write(image, (int)off, take);
        }
        return output.ToArray();
    }

    public static byte[] Sha256(byte[] data)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(data);
    }
}
