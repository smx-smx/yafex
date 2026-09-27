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
// Synthetic IDB tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Idb;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class IdbTests
{
    internal static byte[] BuildImage()
    {
        var img = new List<byte>();
        img.AddRange(Encoding.ASCII.GetBytes("IDB_VERSION=1.1\n"));
        img.AddRange(Encoding.ASCII.GetBytes("IDB_NUM_IMG=3\n"));
        img.AddRange(Encoding.ASCII.GetBytes("IDB_HDR_ENDMARK\n"));
        int tableStart = img.Count;
        var blobs = new[] { new byte[] { 1, 2, 3 }, Array.Empty<byte>(), new byte[] { 4, 5 } };
        int dataStart = tableStart + 3 * 16;
        int off = dataStart;
        foreach (var b in blobs)
        {
            img.AddRange(new byte[8]); // unk1/unk2
            img.AddRange(BitConverter.GetBytes(b.Length));
            img.AddRange(BitConverter.GetBytes(b.Length == 0 ? 0 : off));
            off += b.Length;
        }
        foreach (var b in blobs)
        {
            img.AddRange(b);
        }
        return img.ToArray();
    }

    [Test]
    public void Valid_Detects90()
    {
        var res = (IdbDetectionResult)new IdbAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage()));
        Assert.AreEqual(90, res.Confidence);
        Assert.AreEqual(3, res.Table.ImageCount);
    }

    [Test]
    public void Extract_SkipsZeroAndNumbersOutputs()
    {
        var mem = Mem(BuildImage(), "IDB_X.lr");
        var det = (IdbDetectionResult)new IdbAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new IdbAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(2, artifacts.Count);
        Assert.AreEqual("1.png", artifacts[0].Name);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, artifacts[0].Data.ToArray());
        Assert.AreEqual("2.png", artifacts[1].Name);
        CollectionAssert.AreEqual(new byte[] { 4, 5 }, artifacts[1].Data.ToArray());
    }

    [Test]
    public void Garbage_Detects0()
    {
        var res = new IdbAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(Encoding.ASCII.GetBytes("definitely not an image database....")));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void BadOffset_Detects0()
    {
        var img = BuildImage();
        // corrupt first entry offset past EOF (table starts at 46)
        Array.Copy(BitConverter.GetBytes(0x7FFFFFFF), 0, img, 46 + 12, 4);
        var res = new IdbAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(img));
        Assert.AreEqual(0, res.Confidence);
    }
}
