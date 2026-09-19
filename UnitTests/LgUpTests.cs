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
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using NUnit.Framework;

using Smx.SharpIO.Memory.Buffers;

using Yafex;
using Yafex.FileFormats.LgUp;

namespace UnitTests;

[TestFixture]
public class LgUpTests
{
    private static byte[] RandomKey()
    {
        var k = new byte[16];
        Random.Shared.NextBytes(k);
        return k;
    }

    private static byte[] AesCbcEncrypt(byte[] plain, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = LgUpFormat.Iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var enc = aes.CreateEncryptor();
        var outBuf = new byte[plain.Length];
        var n = enc.TransformBlock(plain, 0, plain.Length, outBuf, 0);
        Assert.AreEqual(plain.Length, n);
        return outBuf;
    }

    private static byte[] PkcsPad(byte[] data)
    {
        var pad = 16 - (data.Length % 16);
        var r = new byte[data.Length + pad];
        data.CopyTo(r, 0);
        for (var i = data.Length; i < r.Length; i++) r[i] = (byte)pad;
        return r;
    }

    private static void PutU32(List<byte> b, uint v) => b.AddRange(BitConverter.GetBytes(v));
    private static void PutU64(List<byte> b, ulong v) => b.AddRange(BitConverter.GetBytes(v));
    private static void PutStr(List<byte> b, string s)
    {
        var raw = Encoding.UTF8.GetBytes(s);
        PutU32(b, (uint)raw.Length);
        b.AddRange(raw);
    }

    /// <summary>Builds a minimal LGUP v4 file: one CInstallFull section, one unit.</summary>
    private static byte[] BuildPackage(byte[] key, byte[] content, out byte[] expected)
    {
        expected = content;
        var encPayload = AesCbcEncrypt(PkcsPad(content), key);

        var sh = new List<byte>();
        PutU32(sh, LgUpFormat.InstallFull);
        PutStr(sh, "test");
        PutStr(sh, "");
        PutU64(sh, (ulong)content.Length);
        PutU32(sh, 0); // FullFlag
        PutU32(sh, 1); // FullCount
        PutU32(sh, (uint)encPayload.Length); // FullUnitSize
        sh.AddRange(new byte[16]); // TargetMd5
        PutU32(sh, (uint)encPayload.Length); // PatchSize
        PutU32(sh, 0); // UncompressedSize
        sh.AddRange(SHA256.HashData(encPayload));

        var dec = new List<byte>();
        PutU32(dec, 0); // HeaderSize placeholder
        dec.AddRange(Encoding.ASCII.GetBytes("LGUP"));
        dec.AddRange(Encoding.ASCII.GetBytes("0400"));
        PutU32(dec, LgUpFormat.CompressNone);
        PutU32(dec, (uint)encPayload.Length); // TotalDataSize
        PutU32(dec, 1); // SectionCount
        PutU32(dec, (uint)sh.Count);
        PutU32(dec, 0); // PatchOffsets[0]
        dec.AddRange(sh);
        var decArr = dec.ToArray();
        BitConverter.GetBytes((uint)decArr.Length).CopyTo(decArr, 0);

        var enc = AesCbcEncrypt(PkcsPad(decArr), key);

        var file = new List<byte>();
        file.AddRange(new byte[0x100]); // signature
        PutU32(file, (uint)(8 + enc.Length)); // EncHeaderSize
        file.AddRange(Encoding.ASCII.GetBytes("EPKG"));
        file.AddRange(enc);
        file.AddRange(encPayload);
        return file.ToArray();
    }

    private static LgUpDetector DetectorWithKey(byte[] key) =>
        new(new KeysRepository(null!), new Dictionary<string, string>
        {
            { "key", Convert.ToHexString(key) }
        });

    [Test]
    public void TestDetect_NoMagic()
    {
        var det = DetectorWithKey(RandomKey());
        var mf = new MemoryDataSource(new Memory64<byte>(new byte[0x200]))
            { Name = "test", Flags = DataSourceFlags.Input };
        var res = (LgUpDetectionResult)det.Detect(mf);
        Assert.IsFalse(res.Succeded());
    }

    [Test]
    public void TestDetect_WrongKey()
    {
        var file = BuildPackage(RandomKey(), Encoding.ASCII.GetBytes("hello-lgup"), out _);
        var det = DetectorWithKey(RandomKey());
        var mf = new MemoryDataSource(new Memory64<byte>(file))
            { Name = "test", Flags = DataSourceFlags.Input };
        var res = (LgUpDetectionResult)det.Detect(mf);
        Assert.AreEqual(30, res.Confidence);
        Assert.IsNull(res.Context);
    }

    [Test]
    public void TestDetectAndExtract_RoundTrip()
    {
        var key = RandomKey();
        var content = new byte[64];
        Random.Shared.NextBytes(content);
        var file = BuildPackage(key, content, out var expected);

        var det = DetectorWithKey(key);
        var mf = new MemoryDataSource(new Memory64<byte>(file))
            { Name = "test.up", Flags = DataSourceFlags.Input };
        var res = (LgUpDetectionResult)det.Detect(mf);
        Assert.AreEqual(100, res.Confidence);
        Assert.NotNull(res.Context);
        Assert.AreEqual(4, res.Context!.Package.Version);
        Assert.AreEqual(1, res.Context.Package.Sections.Count);

        var extractor = new LgUpExtractor(res.Context);
        var artifacts = extractor.Extract(mf).ToList();
        Assert.IsNotEmpty(artifacts);

        var unit = artifacts.FirstOrDefault(a => a.Name.StartsWith("unit_"));
        Assert.NotNull(unit, "unit artifact missing: " + string.Join(",", artifacts.Select(a => a.Name)));
        CollectionAssert.AreEqual(expected, unit!.Data.ToArray());

        var img = artifacts.FirstOrDefault(a => a.Name == "test.img");
        Assert.NotNull(img, "full image artifact missing");
        CollectionAssert.AreEqual(expected, img!.Data.ToArray());
    }
}
