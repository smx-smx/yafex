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
// Shared builders for the synthetic format tests. Everything stays in
// memory: no disk IO, no temp files, no firmware samples, no key files.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;

using Smx.SharpIO.Memory.Buffers;

using Yafex;
using Yafex.Fuse;

namespace UnitTests;

internal static class Synthetic
{
    internal static KeysRepository EmptyKeys() => new KeysRepository(KeyBundle.Empty);

    internal static MemoryDataSource Mem(byte[] bytes, string name = "synthetic")
    {
        return new MemoryDataSource(new Memory64<byte>(bytes))
        {
            Name = name,
            Flags = DataSourceFlags.Input
        };
    }

    /// <summary>
    /// Uniform `--list` output of the lazy tree for a detected image, followed
    /// by the text of every `.meta` sidecar (format headers live there).
    /// </summary>
    internal static string ListTree(IFormatAddon addon, DetectionResult det, IDataSource ds)
    {
        var vfs = VfsMount.CreateLazy(addon, det, ds);
        if (vfs == null) throw new InvalidOperationException("no lazy tree");
        var sw = new StringWriter();
        VfsListing.WriteFlat(vfs.Root, sw);
        foreach (var e in VfsWalk.Walk(vfs.Root).Where(e => !e.IsDirectory && e.Path.EndsWith(".meta")))
        {
            using var r = new StreamReader(VfsWalk.OpenRead(e.Node));
            sw.WriteLine(r.ReadToEnd());
        }
        return sw.ToString();
    }

    internal static byte[] Zeros(int n) => new byte[n];

    internal static void WriteAscii(byte[] buf, int offset, string s)
    {
        var b = Encoding.ASCII.GetBytes(s);
        Array.Copy(b, 0, buf, offset, Math.Min(b.Length, buf.Length - offset));
    }

    internal static void WriteU32LE(byte[] buf, int offset, uint v)
    {
        BitConverter.GetBytes(v).CopyTo(buf, offset);
    }

    internal static byte[] AesCbcEncryptNone(byte[] plain, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var enc = aes.CreateEncryptor();
        return enc.TransformFinalBlock(plain, 0, plain.Length);
    }

    // Test-local inverse of SddlSecExtractor.Decipher: production evolves its
    // keystream on the cipher bytes, so enciphering must evolve on the bytes
    // it emits (mirrors production statement order exactly).
    internal static byte[] SddlEncipher(byte[] plain)
    {
        var dst = new byte[plain.Length];
        uint v3 = 0x388;
        byte j = 0;
        for (int k = 0; k < plain.Length; k++)
        {
            j = (byte)(j + 1);
            byte key = (byte)((v3 & 0xff00) >> 8);
            byte c = (byte)(plain[k] ^ key);
            dst[k] = c;
            uint v11 = (uint)c + 38400u;
            v3 = unchecked(v3 + v11 + 163u);
            if (j == 0)
            {
                v3 = 0x388;
            }
        }
        return dst;
    }
}
