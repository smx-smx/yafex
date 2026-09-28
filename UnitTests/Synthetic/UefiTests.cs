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
// Synthetic UEFI tests. Fully in-memory: firmware volume, FFS file,
// sections, flash descriptor, NVAR entries and FIT are hand-built here;
// no disk, no samples.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Uefi;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class UefiTests
{
    private static readonly Guid TestGuid = new("12345678-1234-5678-9abc-def012345678");

    internal static byte[] BuildVolume(int fileCount = 1)
    {
        var img = new List<byte>();
        img.AddRange(new byte[16]); // ZeroVector
        img.AddRange(new Guid("8c8ce578-8a3d-4f1c-9935-896185c32dd3").ToByteArray()); // FFS2
        int lenPos = img.Count;
        img.AddRange(new byte[8]); // FvLength (patched later)
        img.AddRange(Encoding.ASCII.GetBytes("_FVH"));
        img.AddRange(BitConverter.GetBytes(0x0004FEFFu)); // Attributes
        int hlenPos = img.Count;
        img.AddRange(new byte[2]); // HeaderLength (patched)
        img.AddRange(new byte[2]); // Checksum (patched)
        img.AddRange(new byte[2]); // ExtHeaderOffset = 0
        img.Add(0); // Reserved
        img.Add(2); // Revision
        img.AddRange(BitConverter.GetBytes(1u)); // BlockMap NumBlocks
        img.AddRange(BitConverter.GetBytes(0x1000u)); // BlockMap Length
        img.AddRange(BitConverter.GetBytes(0u));
        img.AddRange(BitConverter.GetBytes(0u));
        int hlen = img.Count;
        img[hlenPos] = (byte)hlen;
        img[hlenPos + 1] = (byte)(hlen >> 8);

        for (int f = 0; f < fileCount; f++)
        {
            int fpos = img.Count;
            img.AddRange(TestGuid.ToByteArray());
            img.Add(0); // header checksum (patched)
            img.Add(0); // file checksum
            img.Add(0x07); // DRIVER
            img.Add(0); // attributes
            int sizePos = img.Count;
            img.AddRange(new byte[3]);
            img.Add(0xF8); // state
            // UI section "TestUi"
            var ui = Encoding.Unicode.GetBytes("TestUi\0");
            int uiSize = 4 + ui.Length;
            img.Add((byte)uiSize);
            img.Add((byte)(uiSize >> 8));
            img.Add((byte)(uiSize >> 16));
            img.Add(0x15);
            img.AddRange(ui);
            while (img.Count % 4 != 0)
            {
                img.Add(0);
            }
            // RAW section {1,2,3,4}
            img.Add(8); img.Add(0); img.Add(0); img.Add(0x19);
            img.AddRange(new byte[] { 1, 2, 3, 4 });
            int size = img.Count - fpos;
            img[sizePos] = (byte)size;
            img[sizePos + 1] = (byte)(size >> 8);
            img[sizePos + 2] = (byte)(size >> 16);
            int sum = 0;
            for (int i = 0; i < 24; i++)
            {
                if (i == 16 || i == 17 || i == 23)
                {
                    continue;
                }
                sum += img[fpos + i];
            }
            img[fpos + 16] = (byte)((256 - (sum & 0xFF)) & 0xFF);
            while (img.Count % 8 != 0)
            {
                img.Add(0xFF);
            }
        }

        long total = img.Count;
        var lenBytes = BitConverter.GetBytes(total);
        for (int i = 0; i < 8; i++)
        {
            img[lenPos + i] = lenBytes[i];
        }
        int csum = 0;
        for (int i = 0; i < hlen; i += 2)
        {
            csum += img[i] | (img[i + 1] << 8);
        }
        csum = (0x10000 - (csum & 0xFFFF)) & 0xFFFF;
        img[hlenPos + 2] = (byte)csum;
        img[hlenPos + 3] = (byte)(csum >> 8);
        return img.ToArray();
    }

    internal static byte[] BuildDescriptorImage()
    {
        var img = new byte[0x6000];
        for (int i = 0; i < img.Length; i++)
        {
            img[i] = 0xFF;
        }
        WriteU32LE(img, 0x10, 0x0FF0A55A);
        // FLMAP0: FCBA=1 (*16 = 0x10), FRBA=4 (*16 = 0x40), NR=1 (2 regions)
        img[0x14] = 1; img[0x15] = 0; img[0x16] = 4; img[0x17] = 1;
        // region 0 (fd): base 0, limit 0 -> 0x0..0xFFF
        WriteU32LE(img, 0x40, 0x00000000u);
        // region 1 (bios): base 1, limit 4 -> 0x1000..0x4FFF
        WriteU32LE(img, 0x44, 0x00040001u);
        var fv = BuildVolume();
        Array.Copy(fv, 0, img, 0x1000, Math.Min(fv.Length, 0x4000));
        return img;
    }

    internal static byte[] BuildNvar()
    {
        var img = new List<byte>();
        // entry 1: ASCII name + data (10-byte header + skip + name + data)
        var name = Encoding.ASCII.GetBytes("VarOne\0");
        int total1 = 10 + 1 + name.Length + 2;
        img.AddRange(Encoding.ASCII.GetBytes("NVAR"));
        img.Add((byte)total1); img.Add((byte)(total1 >> 8));
        img.AddRange(new byte[] { 0, 0, 0 }); // reserved
        img.Add(0x82); // DESC_ASCII | VLD
        img.Add(0); // no-GUID skip byte
        img.AddRange(name);
        img.AddRange(new byte[] { 9, 9 });
        // entry 2: data-only
        int total2 = 10 + 1 + 3;
        img.AddRange(Encoding.ASCII.GetBytes("NVAR"));
        img.Add((byte)total2); img.Add((byte)(total2 >> 8));
        img.AddRange(new byte[] { 0, 0, 0 });
        img.Add(0x88); // DATA | VLD
        img.Add(0);
        img.AddRange(new byte[] { 1, 2, 3 });
        return img.ToArray();
    }

    internal static byte[] BuildFitImage()
    {
        var img = new byte[0x1000];
        for (int i = 0; i < img.Length; i++)
        {
            img[i] = 0xFF;
        }
        // fake microcode at 0x100 (dword 1, TotalSize 48 at +32)
        WriteU32LE(img, 0x100, 1u);
        WriteU32LE(img, 0x100 + 32, 48u);
        // FIT at 0xF00: header + 1 microcode entry
        long fitOff = 0xF00;
        WriteAscii(img, (int)fitOff, "_FIT_   ");
        WriteU32LE(img, (int)fitOff + 8, 2u); // count
        img[fitOff + 12] = 1; // version
        img[fitOff + 14] = 0x00; // FIT_HEADER
        long addr = 0x1_0000_0000L - img.Length + 0x100;
        for (int i = 0; i < 8; i++)
        {
            img[fitOff + 16 + i] = (byte)(addr >> (8 * i));
        }
        img[fitOff + 16 + 14] = 0x01; // MICROCODE
        WriteU32LE(img, img.Length - 0x40, (uint)(0x1_0000_0000L - img.Length + fitOff));
        return img;
    }

    [Test]
    public void Garbage_DoesNotDetect()
    {
        var garbage = new byte[0x1000];
        new Random(7).NextBytes(garbage);
        var res = new UefiDetector().Detect(Mem(garbage));
        Assert.IsFalse(res.Succeded());
    }

    [Test]
    public void Descriptor_Detects90AndSplitsRegions()
    {
        var img = BuildDescriptorImage();
        var det = (UefiDetectionResult)new UefiDetector().Detect(Mem(img));
        Assert.AreEqual(90, det.Confidence);
        Assert.IsTrue(det.HasFlashDescriptor);
        var regions = Uefi.ReadRegions(img);
        Assert.AreEqual(2, regions.Count);
        Assert.AreEqual("bios", regions[1].Name);
        var artifacts = new UefiExtractor(det).Extract(Mem(img)).ToList();
        var names = artifacts.Select(a => a.Name).ToList();
        CollectionAssert.Contains(names, "region-bios.bin");
        CollectionAssert.Contains(names, "region-fd.bin");
        var bios = artifacts.First(a => a.Name == "region-bios.bin");
        Assert.IsTrue(bios.Flags.HasFlag(DataSourceFlags.ProcessFurther));
    }

    [Test]
    public void Volume_DetectsListsExtracts()
    {
        var img = BuildVolume();
        var det = (UefiDetectionResult)new UefiDetector().Detect(Mem(img));
        Assert.IsTrue(det.Succeded());
        Assert.IsFalse(det.HasFlashDescriptor);
        var vols = Uefi.FindVolumes(img);
        Assert.AreEqual(1, vols.Count);
        var files = Uefi.ReadFiles(img, vols[0]);
        Assert.AreEqual(1, files.Count);
        Assert.AreEqual(0x07, files[0].Type);
        var secs = Uefi.ReadSections(img, files[0]);
        Assert.AreEqual(2, secs.Count);
        var out_ = new StringWriter();
        new UefiLister(det).List(Mem(img), det, out_);
        string listing = out_.ToString();
        Assert.IsTrue(listing.Contains("TestUi"), listing);
        Assert.IsTrue(listing.Contains("DRIVER"), listing);
        var artifacts = new UefiExtractor(det).Extract(Mem(img)).ToList();
        var ui = artifacts.First(a => a.Name.EndsWith("-UI.txt"));
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("TestUi"), ui.Data.ToArray());
        var raw = artifacts.First(a => a.Name.EndsWith("-RAW.bin"));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, raw.Data.ToArray());
    }

    [Test]
    public void Nvar_RoundTrips()
    {
        var buf = BuildNvar();
        var vars = Uefi.ReadNvarStore(buf, 0, buf.Length);
        Assert.AreEqual(2, vars.Count);
        Assert.AreEqual("VarOne", vars[0].Name);
        Assert.AreEqual(2, vars[0].DataSize);
        Assert.IsNull(vars[1].Name);
        Assert.AreEqual(3, vars[1].DataSize);
    }

    [Test]
    public void Fit_ListsAndExtractsMicrocode()
    {
        var img = BuildFitImage();
        var entries = Uefi.ReadFit(img);
        Assert.AreEqual(2, entries.Count);
        Assert.AreEqual(0x01, entries[1].Type);
        Assert.IsTrue(Uefi.TryReadMicrocode(img, entries[1], out var ucode));
        Assert.AreEqual(48, ucode.Length);
        var det = (UefiDetectionResult)new UefiDetector().Detect(Mem(img));
        var out_ = new StringWriter();
        new UefiLister(det).List(Mem(img), det, out_);
        Assert.IsTrue(out_.ToString().Contains("FIT microcode"));
        var artifacts = new UefiExtractor(det).Extract(Mem(img)).ToList();
        Assert.AreEqual(1, artifacts.Count(a => a.Name == "fit-00-microcode.bin"));
    }

    [Test]
    public void Lzma_DecodesVector()
    {
        // 24-byte Alone stream for a single 'X' (generated once with
        // CPython's lzma.compress, format=ALONE); self-contained vector.
        var stream = new byte[]
        {
            0x5D, 0x00, 0x00, 0x80, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0x00, 0x2C, 0x41, 0xFB, 0xFF, 0xFF, 0xFF, 0xE0, 0x00, 0x00, 0x00,
        };
        CollectionAssert.AreEqual(new byte[] { (byte)'X' }, LzmaDecoder.Decompress(stream));
    }

    [Test]
    public void Lzma_RejectsGarbage()
    {
        var garbage = new byte[64];
        new Random(11).NextBytes(garbage);
        garbage[0] = 0xFF; // invalid properties byte
        Assert.Throws<InvalidDataException>(() => LzmaDecoder.Decompress(garbage));
    }

    [Test]
    public void CompressType2_LzmaCustom()
    {
        // same single-'X' stream with a known size field (section uclen=1)
        var stream = new byte[]
        {
            0x5D, 0x00, 0x00, 0x80, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x2C, 0x41, 0xFB, 0xFF, 0xFF, 0xFF, 0xE0, 0x00, 0x00, 0x00,
        };
        var info = new Uefi.CompressInfo(1, 2, stream);
        CollectionAssert.AreEqual(new byte[] { (byte)'X' }, Uefi.DecompressSection(info));
    }

    [Test]
    public void GuidedStatic_PassesThrough()
    {
        var img = new List<byte>();
        img.AddRange(UefiStructures.GuidedStatic.ToByteArray());
        img.AddRange(new byte[] { 24, 0, 0, 0 }); // data offset + attributes
        img.AddRange(new byte[] { (byte)'h', (byte)'i', (byte)'!' });
        var bytes = img.ToArray();
        var full = new byte[4 + bytes.Length];
        Array.Copy(bytes, 0, full, 4, bytes.Length);
        var sec = new Uefi.Section(0, 0x02, full.Length, 4, bytes.Length);
        Assert.IsTrue(Uefi.TryReadGuided(full, sec, out var guided));
        Assert.AreEqual(UefiStructures.GuidedStatic, guided.DefinitionGuid);
        CollectionAssert.AreEqual(
            new byte[] { (byte)'h', (byte)'i', (byte)'!' },
            Uefi.DecompressGuided(full, sec, guided));
    }
}
