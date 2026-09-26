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
// Synthetic, fully in-memory format tests: no disk IO, no temp files,
// no firmware samples, no key-bundle files. Covers every registered
// firmware addon (detectors + VFS nodes + key handling + listers).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using NUnit.Framework;

using Smx.SharpIO.Memory.Buffers;

using Yafex;
using Yafex.FileFormats;
using Yafex.FileFormats.EpkV1;
using Yafex.FileFormats.EpkV2;
using Yafex.FileFormats.EpkV3;
using Yafex.FileFormats.FreescaleNand;
using Yafex.FileFormats.LgUp;
using Yafex.FileFormats.LxBoot;
using Yafex.FileFormats.Lzhs;
using Yafex.FileFormats.LzhsFs;
using Yafex.FileFormats.MediatekPkg;
using Yafex.FileFormats.MStarPkg;
using Yafex.FileFormats.Nfwb;
using Yafex.FileFormats.Partinfo;
using Yafex.FileFormats.SddlSec;
using Yafex.FileFormats.Xex;
using Yafex.Fuse;
using Yafex.Support;

namespace UnitTests;

[TestFixture]
public class SyntheticFormatTests
{
    private static KeysRepository EmptyKeys() => new KeysRepository(KeyBundle.Empty);

    private static MemoryDataSource Mem(byte[] bytes, string name = "synthetic")
    {
        return new MemoryDataSource(new Memory64<byte>(bytes))
        {
            Name = name,
            Flags = DataSourceFlags.Input
        };
    }

    private static byte[] Zeros(int n) => new byte[n];

    private static void WriteAscii(byte[] buf, int offset, string s)
    {
        var b = Encoding.ASCII.GetBytes(s);
        Array.Copy(b, 0, buf, offset, Math.Min(b.Length, buf.Length - offset));
    }

    private static void WriteU32LE(byte[] buf, int offset, uint v)
    {
        BitConverter.GetBytes(v).CopyTo(buf, offset);
    }

    // ---------- EpkV1 ----------

    [Test]
    public void EpkV1_PlainMagic_Detects99()
    {
        var buf = Zeros(0x400);
        WriteAscii(buf, 0, "epak");
        var res = (Epk1DetectionResult)new Epk1Detector().Detect(Mem(buf));
        Assert.AreEqual(99, res.Confidence);
    }

    [Test]
    public void EpkV1_Garbage_Detects0()
    {
        var res = (Epk1DetectionResult)new Epk1Detector().Detect(Mem(Zeros(0x400)));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void EpkV1_Lister_OnePak_OutputsPakLine()
    {
        int hdrSize = Marshal.SizeOf<Epk1Header>();
        var buf = Zeros(hdrSize + Marshal.SizeOf<PakHeader>() + 0x10);
        WriteAscii(buf, 0, "epak");
        WriteU32LE(buf, 8, 1); // pakCount = 1 -> Old
        // pakRecs[0] starts at offset 12: offset, size
        WriteU32LE(buf, 12, (uint)hdrSize);
        WriteU32LE(buf, 16, 0x100);
        WriteAscii(buf, hdrSize, "kern");
        WriteU32LE(buf, hdrSize + 4, 0x100);
        var ds = Mem(buf);
        var det = (Epk1DetectionResult)new Epk1Detector().Detect(ds);
        Assert.AreEqual(99, det.Confidence);
        var lister = new Epk1Addon().CreateLister(det);
        Assert.NotNull(lister);
        var sw = new StringWriter();
        lister!.List(ds, det, sw);
        var text = sw.ToString();
        Assert.That(text, Does.Contain("EPKv1"));
        Assert.That(text, Does.Contain("kern.pak"));
    }

    // ---------- EpkV2 / Beta ----------

    [Test]
    public void EpkV2Beta_PlainHeader_Detects100()
    {
        var buf = Zeros(0x400);
        WriteAscii(buf, 0, "epak");
        WriteAscii(buf, 12, "EPK2");
        var det = new Epk2BetaDetector(EmptyKeys());
        var res = (Epk2DetectionResult<EPK_V2_BETA_HEADER>)det.Detect(Mem(buf));
        Assert.AreEqual(100, res.Confidence);
        Assert.NotNull(res.Context);
    }

    [Test]
    public void EpkV2Beta_Garbage_Detects0()
    {
        var det = new Epk2BetaDetector(EmptyKeys());
        var res = (Epk2DetectionResult<EPK_V2_BETA_HEADER>)det.Detect(Mem(Zeros(0x400)));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void EpkV2_PlainWithVersions_Detects180()
    {
        int hdrSize = Marshal.SizeOf<EPK_V2_HEADER>();
        int total = EPK_V2_STRUCTURE.SIGNATURE_SIZE + hdrSize + 256 + 32 + 0x10;
        var buf = Zeros(total);
        int hdrOff = EPK_V2_STRUCTURE.SIGNATURE_SIZE;
        WriteAscii(buf, hdrOff, "epak");
        WriteAscii(buf, hdrOff + 12, "EPK2");
        int verOff = hdrOff + hdrSize + 256;
        WriteAscii(buf, verOff, "06.01.46.01");
        WriteAscii(buf, verOff + 16, "05.05.90.01");
        var det = new Epk2Detector(EmptyKeys());
        DetectionResult res = det.Detect(Mem(buf));
        Assert.AreEqual(180, res.Confidence);
    }

    [Test]
    public void EpkV2_EncryptedWithoutKeys_Detects0Gracefully()
    {
        var buf = Zeros(0x800);
        new Random(42).NextBytes(buf);
        var det = new Epk2Detector(EmptyKeys());
        DetectionResult res = det.Detect(Mem(buf));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void EpkV2Beta_Lister_OutputsHeader()
    {
        var buf = Zeros(0x400);
        WriteAscii(buf, 0, "epak");
        WriteAscii(buf, 12, "EPK2");
        var ds = Mem(buf);
        var det = (Epk2DetectionResult<EPK_V2_BETA_HEADER>)new Epk2BetaDetector(EmptyKeys()).Detect(ds);
        var lister = new Epk2BetaAddon(EmptyKeys()).CreateLister(det);
        Assert.NotNull(lister);
        var sw = new StringWriter();
        lister!.List(ds, det, sw);
        Assert.That(sw.ToString().Length, Is.GreaterThan(0));
    }

    // ---------- EpkV3 (new plaintext + old negative) ----------

    [Test]
    public void EpkV3New_PlainVersions_Detects80()
    {
        int headSize = Marshal.SizeOf<EPK_V3_NEW_HEAD_STRUCTURE>();
        int hdrSize = Marshal.SizeOf<EPK_V3_NEW_HEADER>();
        var buf = Zeros(headSize + 0x300);
        WriteAscii(buf, 256, "EPK3");
        WriteAscii(buf, 256 + hdrSize, "03.53.45.01");
        WriteAscii(buf, 256 + hdrSize + 16, "05.05.90.01");
        var det = new Epk3NewDetector(EmptyKeys());
        var res = (Epk3NewDetectionResult)det.Detect(Mem(buf));
        Assert.AreEqual(80, res.Confidence);
    }

    [Test]
    public void EpkV3New_Garbage_Detects0()
    {
        var det = new Epk3NewDetector(EmptyKeys());
        var res = (Epk3NewDetectionResult)det.Detect(Mem(Zeros(0x800)));
        Assert.AreEqual(0, res.Confidence);
    }

    // ---------- Partinfo ----------

    [Test]
    public void Partinfo_KnownMagics_Detect30WithType()
    {
        foreach (var (magic, _) in new[] { (0x20081118u, 0), (0x20110729u, 0), (0x20120716u, 0) })
        {
            var buf = Zeros(16);
            WriteU32LE(buf, 0, magic);
            var res = (PartinfoDetectionResult)new PartinfoAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(buf));
            Assert.AreEqual(30, res.Confidence);
            Assert.NotNull(res.Context);
        }
    }

    [Test]
    public void Partinfo_Garbage_Detects0()
    {
        var res = (PartinfoDetectionResult)new PartinfoAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(Zeros(16)));
        Assert.AreEqual(0, res.Confidence);
    }

    // ---------- LZHS / LZHSFS (negative paths, no encoder needed) ----------

    [Test]
    public void Lzhs_Garbage_Detects0NoThrow()
    {
        var buf = Zeros(64);
        new Random(123).NextBytes(buf);
        Assert.DoesNotThrow(() =>
        {
            var res = new LzhsDetector().Detect(Mem(buf));
            Assert.AreEqual(0, res.Confidence);
        });
    }

    [Test]
    public void Lzhs_Truncated_Detects0NoThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            var res = new LzhsDetector().Detect(Mem(new byte[4]));
            Assert.AreEqual(0, res.Confidence);
        });
    }

    [Test]
    public void LzhsFs_Short_Detects0()
    {
        var res = new LzhsFsDetector().Detect(Mem(new byte[8]));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void LzhsFs_Garbage_Detects0NoThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            var res = new LzhsFsDetector().Detect(Mem(Zeros(128)));
            Assert.AreEqual(0, res.Confidence);
        });
    }

    // ---------- MStarPkg ----------

    [Test]
    public void MStarPkg_WithMagic_Detects100()
    {
        var buf = Zeros(0x4000);
        WriteAscii(buf, 0, "xxx setenv MstarUpgrade_complete yyy");
        var res = new MStarPkgDetector().Detect(Mem(buf));
        Assert.AreEqual(100, res.Confidence);
    }

    [Test]
    public void MStarPkg_WithoutMagic_Detects0()
    {
        var res = new MStarPkgDetector().Detect(Mem(Zeros(0x4000)));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void MStarPkg_Short_Detects0()
    {
        var res = new MStarPkgDetector().Detect(Mem(new byte[16]));
        Assert.AreEqual(0, res.Confidence);
    }

    // ---------- FreescaleNand ----------

    [Test]
    public void FreescaleNand_FcbMagic_Detects100()
    {
        var buf = Zeros(0x40);
        WriteAscii(buf, 0x10, "FCB ");
        var res = new FreescaleNandDetector().Detect(Mem(buf));
        Assert.AreEqual(100, res.Confidence);
    }

    [Test]
    public void FreescaleNand_Garbage_Fails()
    {
        var res = new FreescaleNandDetector().Detect(Mem(Zeros(0x40)));
        Assert.IsFalse(res.Succeded());
    }

    // ---------- Xex ----------

    [Test]
    public void Xex_Magic_Detects50()
    {
        var buf = Zeros(256);
        WriteAscii(buf, 0, "XEX2");
        var res = new Yafex.FileFormats.Xex.XexDetector().Detect(Mem(buf));
        Assert.AreEqual(50, res.Confidence);
    }

    [Test]
    public void Xex_Garbage_Detects0()
    {
        var res = new Yafex.FileFormats.Xex.XexDetector().Detect(Mem(Zeros(256)));
        Assert.AreEqual(0, res.Confidence);
    }

    // ---------- LxSecureBoot (old + new, no keys) ----------

    private static byte[] LxBootSynthetic(bool isNew)
    {
        var buf = Zeros(0x600);
        // NOTE: "\x13A0" would parse as U+13A0; write control bytes explicitly.
        buf[0] = (byte)'H'; buf[1] = 0x13; buf[2] = (byte)'A'; buf[3] = (byte)'0';
        buf[4] = (byte)'M'; buf[5] = 0x16; buf[6] = (byte)'P'; buf[7] = (byte)'R';
        WriteAscii(buf, isNew ? 0x240 : 0xD0, "SIC!");
        // descriptors are zeros (LoadAddress 0 -> second stage scan finds nothing -> null)
        return buf;
    }

    [Test]
    public void LxSecureBoot_OldVariant_Detects20()
    {
        var addon = new Yafex.FileFormats.LxBoot.LxSecureBootAddon();
        var det = addon.CreateDetector(new Dictionary<string, string>());
        var res = (LxSecureBootDetectionResult)det.Detect(Mem(LxBootSynthetic(false)));
        Assert.AreEqual(60, res.Confidence); // 20 base + 20 + 20 known magics
        Assert.NotNull(res.Context);
    }

    [Test]
    public void LxSecureBoot_NewVariant_Detects20Plus()
    {
        var addon = new Yafex.FileFormats.LxBoot.LxSecureBootAddon();
        var det = addon.CreateDetector(new Dictionary<string, string>());
        var res = (LxSecureBootDetectionResult)det.Detect(Mem(LxBootSynthetic(true)));
        Assert.That(res.Confidence, Is.GreaterThan(0));
        Assert.NotNull(res.Context);
    }

    [Test]
    public void LxSecureBoot_NoSic_Fails()
    {
        var addon = new Yafex.FileFormats.LxBoot.LxSecureBootAddon();
        var det = addon.CreateDetector(new Dictionary<string, string>());
        var res = det.Detect(Mem(Zeros(0x600)));
        Assert.IsFalse(res.Succeded());
    }

    [Test]
    public void LxSecureBoot_Truncated_FailsGracefully()
    {
        var addon = new Yafex.FileFormats.LxBoot.LxSecureBootAddon();
        var det = addon.CreateDetector(new Dictionary<string, string>());
        Assert.DoesNotThrow(() =>
        {
            var res = det.Detect(Mem(new byte[4]));
            Assert.IsFalse(res.Succeded());
        });
    }

    // ---------- Nfwb ----------

    [Test]
    public void Nfwb_Magic_Detects90()
    {
        var buf = Zeros(200);
        WriteAscii(buf, 0, "NFWB");
        var res = new NfwbDetector().Detect(Mem(buf));
        Assert.AreEqual(90, res.Confidence);
    }

    [Test]
    public void Nfwb_Garbage_Detects0()
    {
        var res = new NfwbDetector().Detect(Mem(Zeros(200)));
        Assert.AreEqual(0, res.Confidence);
    }

    // ---------- MediatekPkg (negative only: needs bundle file for positives) ----------

    [Test]
    public void MediatekPkg_Random_Detects0Gracefully()
    {
        var det = new MediatekPkgDetector(EmptyKeys());
        Assert.DoesNotThrow(() =>
        {
            var res = det.Detect(Mem(Zeros(0x200)));
            Assert.AreEqual(0, res.Confidence);
        });
    }

    // ---------- SddlSec (enciphered synthetic header, statics) ----------

    // Test-local inverse of SddlSecExtractor.Decipher: production evolves its
    // keystream on the cipher bytes, so enciphering must evolve on the bytes
    // it emits (mirrors production statement order exactly).
    private static byte[] SddlEncipher(byte[] plain)
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

    [Test]
    public void SddlSec_Decipher_IsDeterministic()
    {
        var plain = new Memory64<byte>(Encoding.ASCII.GetBytes("HelloSddlSec!!!!!"));
        var a = SddlSecExtractor.Decipher(plain).ToArray();
        var b = SddlSecExtractor.Decipher(plain).ToArray();
        CollectionAssert.AreEqual(a, b);
        Assert.AreEqual(plain.Length, a.Length);
    }

    [Test]
    public void SddlSec_EncipherRoundTripsViaDecipher()
    {
        var plain = Encoding.ASCII.GetBytes("HelloSddlSec!!!!!");
        var back = SddlSecExtractor.Decipher(new Memory64<byte>(SddlEncipher(plain))).ToArray();
        CollectionAssert.AreEqual(plain, back);
    }

    [Test]
    public void SddlSec_DecompressZlib_RoundTrips()
    {
        var raw = Encoding.UTF8.GetBytes(new string('A', 1000));
        byte[] comp;
        using (var ms = new MemoryStream())
        {
            using (var ds = new DeflateStream(ms, CompressionLevel.Fastest))
                ds.Write(raw, 0, raw.Length);
            comp = ms.ToArray();
        }
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
    public void SddlSec_SyntheticHeader_Detects100()
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
    public void SddlSec_Garbage_Detects0()
    {
        var det = new SddlSecDetector(new Dictionary<string, string>());
        var res = (SddlSecDetectionResult)det.Detect(Mem(Zeros(64)));
        Assert.AreEqual(0, res.Confidence);
    }

    // ---------- LgUp (no-keys path; positives covered by LgUpTests) ----------

    [Test]
    public void LgUp_MagicWithoutKeys_Detects30NullCtx()
    {
        var buf = Zeros(0x200);
        WriteAscii(buf, 0x100 + 4, "EPKG");
        var det = new LgUpDetector(EmptyKeys(), new Dictionary<string, string>());
        var res = (LgUpDetectionResult)det.Detect(Mem(buf));
        Assert.AreEqual(30, res.Confidence);
        Assert.IsNull(res.Context);
    }

    [Test]
    public void LgUp_NoMagic_Detects0()
    {
        var det = new LgUpDetector(EmptyKeys(), new Dictionary<string, string>());
        var res = (LgUpDetectionResult)det.Detect(Mem(Zeros(0x200)));
        Assert.AreEqual(0, res.Confidence);
    }

    // ---------- Squashfs (disk-only; assert graceful failure without disk) ----------

    [Test]
    public void Squashfs_MemorySource_ThrowsNotSupported()
    {
        var det = new Yafex.FileFormats.Squashfs.SquashfsDetector();
        Assert.Throws<NotSupportedException>(() => det.Detect(Mem(Zeros(256))));
    }

    // ---------- KeyTrial (no bundle files) ----------

    [Test]
    public void KeyTrial_ExplicitHexKey_Wins()
    {
        var args = new Dictionary<string, string> { { "key", new string('A', 32) } };
        var keys = KeyTrial.ResolveCandidateKeys(args, EmptyKeys(), "lgup-keys");
        Assert.AreEqual(1, keys.Count);
        Assert.AreEqual(16, keys[0].Length);
    }

    [Test]
    public void KeyTrial_InvalidHex_YieldsEmpty()
    {
        var args = new Dictionary<string, string> { { "key", "ZZZ" } };
        var keys = KeyTrial.ResolveCandidateKeys(args, EmptyKeys(), "no-such-collection");
        Assert.IsEmpty(keys);
    }

    [Test]
    public void KeyTrial_MissingCollection_YieldsEmpty()
    {
        var keys = KeyTrial.ResolveCandidateKeys(new Dictionary<string, string>(), EmptyKeys(), "no-such-collection");
        Assert.IsEmpty(keys);
    }

    // ---------- VFS nodes (in-memory, no mount) ----------

    [Test]
    public void VfsNodes_AllAddons_ReturnNonNull()
    {
        var ds = Mem(Zeros(0x4000), "vfs-probe");
        var keys = EmptyKeys();
        var addons = new IFormatAddon[]
        {
            new Epk1Addon(),
            new Epk2Addon(keys),
            new Epk2BetaAddon(keys),
            new Epk3NewAddon(keys),
            new PartinfoAddon(),
            new LzhsAddon(),
            new MStarPkgAddon(),
            new FreescaleNandAddon(),
            new Yafex.FileFormats.Xex.XexAddon(),
            new Yafex.FileFormats.LxBoot.LxSecureBootAddon(),
            new NfwbAddon(),
            new MediatekPkgAddon(keys),
            new LzhsFsAddon(),
            new SddlSecAddon(keys),
            new LgUpAddon(keys),
        };
        foreach (var addon in addons)
        {
            Assert.DoesNotThrow(() =>
            {
                var node = addon.CreateVfsNode(ds);
                Assert.NotNull(node);
            }, $"CreateVfsNode failed for {addon.GetType().Name}");
        }
    }

    [Test]
    public void Vfs_YafexVfs_AddFileAndWalk()
    {
        var vfs = new YafexVfs();
        var ds = Mem(Encoding.ASCII.GetBytes("payload"), "hello.bin");
        vfs.AddDataSource(ds);
        vfs.Root.EnsureLoaded();
        Assert.That(vfs.Root.Tree.Keys, Does.Contain("hello.bin"));
        var node = vfs.Root.Tree["hello.bin"].Node;
        Assert.AreEqual(7, node.Size);
    }

    [Test]
    public void Vfs_LazyDirectory_LoadsOnceInMemory()
    {
        var calls = 0;
        var dir = new LazyVfsDirectory("lazy", Helpers.OctalLiteral(0755), () =>
        {
            calls++;
            return new IVfsNode[] { new YafexDirectory("child", Helpers.OctalLiteral(0755)) };
        });
        dir.EnsureLoaded();
        dir.EnsureLoaded();
        Assert.AreEqual(1, calls);
        Assert.That(dir.Tree.Keys, Does.Contain("child"));
    }
}
