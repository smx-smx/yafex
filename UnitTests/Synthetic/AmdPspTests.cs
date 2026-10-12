#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
// Synthetic AMD PSP tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.AmdPsp;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class AmdPspTests
{
    internal static byte[] BuildPspDir((byte Type, uint Size, ulong Addr)[] entries)
    {
        var buf = new byte[16 + entries.Length * 16];
        WriteAscii(buf, 0, "$PSP");
        WriteU32LE(buf, 8, (uint)entries.Length);
        for (int i = 0; i < entries.Length; i++)
        {
            int o = 16 + i * 16;
            buf[o] = entries[i].Type;
            WriteU32LE(buf, o + 4, entries[i].Size);
            var a = entries[i].Addr;
            for (int b = 0; b < 8; b++)
            {
                buf[o + 8 + b] = (byte)(a >> (8 * b));
            }
        }
        uint cksum = AmdPsp.Fletcher32(buf, 8, buf.Length - 8);
        WriteU32LE(buf, 4, cksum);
        return buf;
    }

    internal static byte[] BuildImageWithEfs(byte[] dir, int efsOffset = 0x100, int dirOffset = 0x1000)
    {
        var buf = new byte[dirOffset + dir.Length];
        WriteU32LE(buf, efsOffset, AmdPsp.EfsSignature);
        WriteU32LE(buf, efsOffset + 0x14, (uint)dirOffset);
        Array.Copy(dir, 0, buf, dirOffset, dir.Length);
        return buf;
    }

    [Test]
    public void ValidEfsAndPsp_Detects95()
    {
        var payload = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        var dir = BuildPspDir(new[] { ((byte)0x04, (uint)payload.Length, (ulong)0x500 | (1UL << 62)) });
        var full = BuildImageWithEfs(dir);
        Array.Copy(payload, 0, full, 0x500, payload.Length);
        var res = (AmdPspDetectionResult)new AmdPspAddon()
            .CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(full));
        Assert.AreEqual(95, res.Confidence);
        Assert.AreEqual(1, res.Directories.Count);
        Assert.AreEqual(1, res.EfsList.Count);
    }

    [Test]
    public void BarePspDir_Detects90()
    {
        var dir = BuildPspDir(new[] { ((byte)0x04, (uint)4, (ulong)0x30 | (1UL << 62)) });
        var buf = new byte[0x100];
        Array.Copy(dir, buf, dir.Length);
        new byte[] { 1, 2, 3, 4 }.CopyTo(buf, 0x30);
        var res = (AmdPspDetectionResult)new AmdPspAddon()
            .CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(buf));
        Assert.AreEqual(90, res.Confidence);
    }

    [Test]
    public void BadChecksum_Detects0()
    {
        var dir = BuildPspDir(new[] { ((byte)0x04, (uint)4, (ulong)0x30 | (1UL << 62)) });
        dir[20] ^= 0xFF;
        var res = new AmdPspAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(dir));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Garbage_Detects0()
    {
        var res = new AmdPspAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(Zeros(0x100)));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Truncated_Detects0()
    {
        var res = new AmdPspAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(Zeros(10)));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void RandomMagicWithoutChecksum_Detects0()
    {
        // byte-hit without a valid checksum (like $PSP inside ARM blobs)
        var buf = Zeros(0x100);
        WriteAscii(buf, 0x40, "$PSP");
        buf[0x40 + 8] = 4;
        var res = new AmdPspAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(buf));
        Assert.AreEqual(0, res.Confidence);
    }
}
