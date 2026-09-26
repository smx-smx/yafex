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
// Synthetic MTK PKG old tests (header vector encrypted with an
// independent Feistel implementation). Fully in-memory.
using System;
using System.IO;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.MediatekPkgOld;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class MediatekPkgOldTests
{
    // decrypts (old header key) to "TPV\0" + "#DH@FiRm" + "2.25" header
    private const string EncHeaderHex =
        "875e648730c0d2c54b87d45536cc0aa375d85304473c1635ccfd7fbc4483c0b327d4cc16eed0cad53394545f2720564819c67670918b8da802f8af41d87ae30df6bb8866407f9760505259e8f6d0b579d8303361a38330084ae3b022c5f443f58afa27305287081cbb1f87999c62e3651c6146183d5ed906c448ec1f611dc3549e29c2a8ea4fdb42d6acbd2564682288aedd2f7b575fe5be";

    private static KeysRepository OldKeys() => new KeysRepository(new KeyBundle(new KeyDTO[]
    {
        new RawKeyDTO { Id = "mtkpkg-old-header-key", KeyMaterial = "09291094", KeyIV = "757CBE04" },
        new RawKeyDTO { Id = "mtkpkg-old-data-iv", KeyMaterial = "727CBE04" },
    }));

    private static byte[] OldFile(long headerOffset = 0)
    {
        var head = Convert.FromHexString(EncHeaderHex);
        var buf = Zeros((int)headerOffset + MtkOldHeader.SIZE + 12);
        Array.Copy(head, 0, buf, (int)headerOffset, head.Length);
        WriteAscii(buf, (int)headerOffset + MtkOldHeader.SIZE, "test");
        return buf;
    }

    [Test]
    public void Detector_Positive_Detects100()
    {
        var det = new MediatekPkgOldDetector(OldKeys());
        var res = (MediatekPkgOldDetectionResult)det.Detect(Mem(OldFile()));
        Assert.AreEqual(100, res.Confidence);
        Assert.NotNull(res.Context);
        Assert.AreEqual(0, res.Context!.HeaderOffset);
        Assert.AreEqual(MtkOldHeader.MTK_MAGIC, res.Context.Header.MtkMagic);
    }

    [Test]
    public void Detector_Offset128_Detects100()
    {
        var det = new MediatekPkgOldDetector(OldKeys());
        var res = (MediatekPkgOldDetectionResult)det.Detect(Mem(OldFile(128)));
        Assert.AreEqual(100, res.Confidence);
        Assert.AreEqual(128, res.Context!.HeaderOffset);
    }

    [Test]
    public void Detector_NoKeys_Detects0()
    {
        var det = new MediatekPkgOldDetector(EmptyKeys());
        var res = (MediatekPkgOldDetectionResult)det.Detect(Mem(OldFile()));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Detector_Random_Detects0Gracefully()
    {
        var buf = Zeros(0x200);
        new Random(7).NextBytes(buf);
        var det = new MediatekPkgOldDetector(OldKeys());
        Assert.DoesNotThrow(() =>
        {
            var res = (MediatekPkgOldDetectionResult)det.Detect(Mem(buf));
            Assert.AreEqual(0, res.Confidence);
        });
    }

    [Test]
    public void Lister_OnePart_OutputsPakLine()
    {
        var keys = OldKeys();
        var ds = Mem(OldFile());
        var det = (MediatekPkgOldDetectionResult)new MediatekPkgOldDetector(keys).Detect(ds);
        Assert.AreEqual(100, det.Confidence);
        var lister = new MediatekPkgOldAddon(keys).CreateLister(det);
        Assert.NotNull(lister);
        var sw = new StringWriter();
        lister!.List(ds, det, sw);
        var text = sw.ToString();
        Assert.That(text, Does.Contain("MediatekPkgOld"));
        Assert.That(text, Does.Contain("test.pak"));
    }
}
