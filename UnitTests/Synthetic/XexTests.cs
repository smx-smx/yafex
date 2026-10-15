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
// Synthetic XEX2 tests: a minimal XEX2 image (header + FILE_FORMAT_INFO +
// security blob + little-endian inner PE with .text/.idata) is built
// in-memory against the Yafex-native driver. Fully in-memory, no fixtures.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Xex;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class XexTests
{
    // Inner PE layout (little-endian, loaded-image addresses).
    private const uint TextVa = 0x200;
    private const uint IdataVa = 0x300;
    private const uint TextRaw = 0x200;
    private const uint IdataRaw = 0x400;
    private static readonly byte[] TextBytes =
        Encoding.ASCII.GetBytes("SYNTHETIC-TEXT!!");
    private static readonly byte[] IdataBytes =
        Encoding.ASCII.GetBytes("SYNTHETIC-IDATA!!");

    private static void PutU16LE(List<byte> b, ushort v) => b.AddRange(BitConverter.GetBytes(v));
    private static void PutU32LE(List<byte> b, uint v) => b.AddRange(BitConverter.GetBytes(v));
    private static void PutU32BE(List<byte> b, uint v)
    {
        b.Add((byte)(v >> 24));
        b.Add((byte)(v >> 16));
        b.Add((byte)(v >> 8));
        b.Add((byte)v);
    }

    private static void PutU16BE(List<byte> b, ushort v)
    {
        b.Add((byte)(v >> 8));
        b.Add((byte)v);
    }

    private static byte[] BuildPe()
    {
        var b = new List<byte>();
        // DOS header (64 bytes, e_lfanew at +60).
        b.AddRange(Encoding.ASCII.GetBytes("MZ"));
        while (b.Count < 60) b.Add(0);
        PutU32LE(b, 64);
        // NT headers at +64: signature, file header, optional header.
        b.AddRange(new byte[] { (byte)'P', (byte)'E', 0, 0 });
        PutU16LE(b, 0x01F2); // PowerPCBE
        PutU16LE(b, 2); // sections
        PutU32LE(b, 0); // TimeDateStamp
        PutU32LE(b, 0); // PointerToSymbolTable
        PutU32LE(b, 0); // NumberOfSymbols
        PutU16LE(b, 224); // SizeOfOptionalHeader
        PutU16LE(b, 0x0100); // 32BIT_MACHINE
        // Optional header (224 bytes): magic at +0, subsystem at +68,
        // NumberOfRvaAndSizes at +88, 16 data directories at +96.
        var opt = new List<byte>();
        PutU16LE(opt, 0x10B);
        while (opt.Count < 68) opt.Add(0);
        PutU16LE(opt, 14); // IMAGE_SUBSYSTEM_XBOX
        while (opt.Count < 88) opt.Add(0);
        PutU32LE(opt, 16);
        while (opt.Count < 224) opt.Add(0);
        b.AddRange(opt);
        // Section headers: .text then .idata (BuildFinalPE needs .idata).
        PutSection(b, ".text", (uint)TextBytes.Length, TextVa, (uint)TextBytes.Length, TextRaw);
        PutSection(b, ".idata", (uint)IdataBytes.Length, IdataVa, (uint)IdataBytes.Length, IdataRaw);
        // Image body: headers are copied verbatim up to min(VA).
        while (b.Count < TextVa) b.Add(0);
        b.AddRange(TextBytes);
        while (b.Count < IdataVa) b.Add(0);
        b.AddRange(IdataBytes);
        return b.ToArray();
    }

    private static void PutSection(List<byte> b, string name, uint vsize, uint va, uint rawSize, uint rawPtr)
    {
        var nb = new byte[8];
        Encoding.ASCII.GetBytes(name).CopyTo(nb, 0);
        b.AddRange(nb);
        PutU32LE(b, vsize);
        PutU32LE(b, va);
        PutU32LE(b, rawSize);
        PutU32LE(b, rawPtr);
        PutU32LE(b, 0); // PointerToRelocations
        PutU32LE(b, 0); // PointerToLinenumbers
        PutU16LE(b, 0); // NumberOfRelocations
        PutU16LE(b, 0); // NumberOfLinenumbers
        PutU32LE(b, 0); // Characteristics
    }

    // 388-byte zeroed security blob; pageDescriptors adds page_descriptor
    // entries (24 bytes each: value + 20-byte digest).
    private static byte[] BuildSecurity(uint pageDescriptors = 0)
    {
        var b = new List<byte>();
        PutU32BE(b, 0); // header_size
        PutU32BE(b, 0); // image_size
        b.AddRange(new byte[0x100]); // rsa_signature
        PutU32BE(b, 0); // unk_108
        PutU32BE(b, 0); // image_flags
        PutU32BE(b, 0); // load_address (<= 0x90000000 -> 64KB pages)
        b.AddRange(new byte[0x14]); // section_digest
        PutU32BE(b, 0); // import_table_count
        b.AddRange(new byte[0x14]); // import_table_digest
        b.AddRange(new byte[0x10]); // xgd2_media_id
        b.AddRange(new byte[0x10]); // aes_key (ECB-decrypted, unused for NONE)
        PutU32BE(b, 0); // export_table
        b.AddRange(new byte[0x14]); // header_digest
        PutU32BE(b, 0); // region
        PutU32BE(b, 0); // allowed_media_types
        PutU32BE(b, pageDescriptors); // page_descriptor_count
        for (int i = 0; i < pageDescriptors; i++)
        {
            PutU32BE(b, 0x11); // 1 page, CODE section
            b.AddRange(new byte[0x14]);
        }
        return b.ToArray();
    }

    // compression: 0 = NONE, 1 = BASIC (single block covering the image).
    private static byte[] BuildXex(byte[] pe, ushort compression, out int headerSize)
    {
        var b = new List<byte>();
        int formatInfoSize = compression == 1 ? 16 : 8;
        int securityOffset = 24 + 8 + formatInfoSize;
        var sec = BuildSecurity(pageDescriptors: compression == 1 ? 1u : 0u);
        headerSize = securityOffset + sec.Length;
        b.AddRange(Encoding.ASCII.GetBytes("XEX2"));
        PutU32BE(b, 0); // module_flags (not a patch)
        PutU32BE(b, (uint)headerSize);
        PutU32BE(b, 0); // reserved
        PutU32BE(b, (uint)securityOffset);
        PutU32BE(b, 1); // header_count
        PutU32BE(b, 0x3FF); // FILE_FORMAT_INFO key
        PutU32BE(b, 32); // value = offset of the format-info struct
        PutU32BE(b, (uint)(8 + (compression == 1 ? 8 : 0))); // info_size
        PutU16BE(b, 0); // encryption NONE
        PutU16BE(b, compression);
        if (compression == 1)
        {
            PutU32BE(b, (uint)pe.Length); // block data_size
            PutU32BE(b, 0); // block zero_size
        }
        b.AddRange(sec);
        b.AddRange(pe);
        return b.ToArray();
    }

    [Test]
    public void Detector_HitsXex2()
    {
        var img = BuildXex(BuildPe(), 0, out _);
        var res = new XexDetector().Detect(Mem(img));
        Assert.AreEqual(50, res.Confidence);
    }

    [Test]
    public void Detector_MissesGarbage()
    {
        var rnd = new byte[512];
        new Random(7).NextBytes(rnd);
        var res = new XexDetector().Detect(Mem(rnd));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Extract_Uncompressed_RebuildsInnerPe()
    {
        var img = BuildXex(BuildPe(), 0, out _);
        var mem = Mem(img, "sample.xex");
        mem.Directory = "sample.xex";
        var artifacts = new XexExtractor().Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual("sample.exe", artifacts[0].Name);
        var exe = artifacts[0].Data.ToArray();
        // MZ + section payloads relocated to their raw offsets.
        Assert.AreEqual((byte)'M', exe[0]);
        Assert.AreEqual((byte)'Z', exe[1]);
        CollectionAssert.AreEqual(TextBytes, exe.Skip((int)TextRaw).Take(TextBytes.Length).ToArray());
        CollectionAssert.AreEqual(IdataBytes, exe.Skip((int)IdataRaw).Take(IdataBytes.Length).ToArray());
    }

    [Test]
    public void Extract_BasicCompressed_RebuildsInnerPe()
    {
        var img = BuildXex(BuildPe(), 1, out _);
        var mem = Mem(img, "sample.xex");
        mem.Directory = "sample.xex";
        var artifacts = new XexExtractor().Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        var exe = artifacts[0].Data.ToArray();
        Assert.AreEqual((byte)'M', exe[0]);
        Assert.AreEqual((byte)'Z', exe[1]);
        CollectionAssert.AreEqual(TextBytes, exe.Skip((int)TextRaw).Take(TextBytes.Length).ToArray());
    }

    [Test]
    public void Extract_Truncated_Throws()
    {
        // XEX2 magic but no room for headers: detector still fires
        // (length > 200), the extractor must reject it, not crash blindly.
        var buf = new byte[300];
        Encoding.ASCII.GetBytes("XEX2").CopyTo(buf, 0);
        var mem = Mem(buf, "cut.xex");
        Assert.AreEqual(50, new XexDetector().Detect(mem).Confidence);
        Assert.Throws<ArgumentOutOfRangeException>(() => new XexExtractor().Extract(mem).ToList());
    }
}
