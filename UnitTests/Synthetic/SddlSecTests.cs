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
// Synthetic SddlSec tests. Fully in-memory: no disk, no samples.
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

using NUnit.Framework;

using Smx.SharpIO.Memory.Buffers;

using Yafex;
using Yafex.FileFormats.SddlSec;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class SddlSecTests
{
    [Test]
    public void Decipher_IsDeterministic()
    {
        var plain = new Memory64<byte>(Encoding.ASCII.GetBytes("HelloSddlSec!!!!!"));
        var a = SddlSecExtractor.Decipher(plain).ToArray();
        var b = SddlSecExtractor.Decipher(plain).ToArray();
        CollectionAssert.AreEqual(a, b);
        Assert.AreEqual(plain.Length, a.Length);
    }

    [Test]
    public void EncipherRoundTripsViaDecipher()
    {
        var plain = Encoding.ASCII.GetBytes("HelloSddlSec!!!!!");
        var back = SddlSecExtractor.Decipher(new Memory64<byte>(SddlEncipher(plain))).ToArray();
        CollectionAssert.AreEqual(plain, back);
    }

    [Test]
    public void DecompressZlib_RoundTrips()
    {
        var raw = Encoding.UTF8.GetBytes(new string('A', 1000));
        // wrap raw deflate in zlib header/adler like DecompressZlib expects (ZLibStream handles it)
        byte[] zlib;
        using (var ms = new MemoryStream())
        {
            using (var zs = new ZLibStream(ms, CompressionMode.Compress))
                zs.Write(raw, 0, raw.Length);
            zlib = ms.ToArray();
        }
        var back = SddlSecExtractor.DecompressZlib(new Memory64<byte>(zlib));
        CollectionAssert.AreEqual(raw, back.ToArray());
    }

    [Test]
    public void SyntheticHeader_Detects100()
    {
        int hdrSize = Marshal.SizeOf<SddlSecHeader>();
        var plain = Zeros(hdrSize + 0x10);
        plain[0] = 0x11; plain[1] = 0x22; plain[2] = 0x33; plain[3] = 0x44;
        WriteAscii(plain, 8, "0001");
        WriteAscii(plain, 12, "0000");
        var enc = SddlEncipher(plain);
        var det = new SddlSecDetector(new Dictionary<string, string>());
        var res = (SddlSecDetectionResult)det.Detect(Mem(enc));
        Assert.AreEqual(100, res.Confidence);
        Assert.NotNull(res.Context);
    }

    [Test]
    public void Garbage_Detects0()
    {
        var det = new SddlSecDetector(new Dictionary<string, string>());
        var res = (SddlSecDetectionResult)det.Detect(Mem(Zeros(64)));
        Assert.AreEqual(0, res.Confidence);
    }
}
