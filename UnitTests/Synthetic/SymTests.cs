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
// Synthetic SYM tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Sym;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class SymTests
{
    internal static byte[] BuildImage(bool hash = false, bool dwarf = false)
    {
        var names = new[] { "main", "NANOX_IsScreenReversed" };
        var img = new List<byte>();
        void U32(uint v) => img.AddRange(BitConverter.GetBytes(v));
        U32(0xB12791EE);
        U32(0);
        int sizePos = img.Count;
        U32(0);
        U32((uint)names.Length);
        U32(0); // tail patched later
        // name offsets are relative to the post-sections base: the has_dwarf
        // slot when flat, past the dwarf header when present (wild files are
        // all hash=2/dwarf=1 with post-header-relative offsets)
        int nameOff = dwarf ? 0 : 4;
        var offs = new List<int>();
        foreach (var n in names)
        {
            U32(0x400000);
            U32(0x400100);
            U32((uint)nameOff);
            offs.Add(nameOff);
            nameOff += Encoding.ASCII.GetByteCount(n) + 1;
        }
        if (hash)
        {
            U32(2);
            U32(0x12345678);
            U32(0x9ABCDEF0);
        }
        else
        {
            U32(0);
        }
        int namesAt;
        if (dwarf)
        {
            U32(1);
            U32(0); // n_dwarf
            U32(0); // dwarf_size
            namesAt = img.Count;
        }
        else
        {
            namesAt = img.Count;
            U32(0);
        }
        foreach (var n in names)
        {
            img.AddRange(Encoding.ASCII.GetBytes(n));
            img.Add(0);
        }
        var bytes = img.ToArray();
        Array.Copy(BitConverter.GetBytes((uint)(bytes.Length - 20)), 0, bytes, sizePos, 4);
        // tail covers everything past the entries (hash/dwarf/names)
        int tableEnd = 20 + names.Length * 12;
        Array.Copy(BitConverter.GetBytes((uint)(bytes.Length - tableEnd)), 0, bytes, 16, 4);
        return bytes;
    }

    [Test]
    public void Valid_Detects90()
    {
        foreach (bool hash in new[] { false, true })
        {
            foreach (bool dwarf in new[] { false, true })
            {
                var res = (SymDetectionResult)new SymAddon().CreateDetector(new Dictionary<string, string>())
                    .Detect(Mem(BuildImage(hash, dwarf)));
                Assert.AreEqual(90, res.Confidence, $"hash={hash} dwarf={dwarf}");
                Assert.AreEqual(2, res.Symbols.Count);
                Assert.AreEqual("main", res.Symbols[0].Name);
                Assert.AreEqual(0x400000u, res.Symbols[0].Addr);
            }
        }
    }

    [Test]
    public void Extract_EmitsIdcAndGhidra()
    {
        var mem = Mem(BuildImage(), "RELEASE.sym");
        var det = (SymDetectionResult)new SymAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new SymAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(2, artifacts.Count);
        Assert.AreEqual("RELEASE.idc", artifacts[0].Name);
        var idc = Encoding.ASCII.GetString(artifacts[0].Data.ToArray());
        Assert.IsTrue(idc.Contains("#include <idc.idc>"));
        Assert.IsTrue(idc.Contains("MakeNameEx( 0x400000, \"main\", SN_NOWARN | SN_CHECK);"));
        Assert.IsTrue(idc.Contains("MakeFunction(0x400000, 0x400100);"));
        Assert.AreEqual("RELEASE_ghidra.py", artifacts[1].Name);
        var py = Encoding.ASCII.GetString(artifacts[1].Data.ToArray());
        Assert.IsTrue(py.Contains("(0x400000, 0x400100, \"main\")"));
        Assert.IsTrue(py.Contains("createFunction(a, name)"));
    }

    [Test]
    public void Garbage_Detects0()
    {
        var res = new SymAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(Encoding.ASCII.GetBytes("definitely not a symbol table...")));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void BadSize_Detects0()
    {
        var img = BuildImage();
        img[8] ^= 0xFF;
        var res = new SymAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(img));
        Assert.AreEqual(0, res.Confidence);
    }
}
