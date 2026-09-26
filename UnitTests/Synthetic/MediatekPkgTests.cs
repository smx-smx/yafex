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
// Synthetic MediatekPkg tests (standard-key positive via in-memory bundle).
// Fully in-memory: no disk, no samples, no bundle files on disk.
using System;
using System.Runtime.CompilerServices;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.MediatekPkg;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class MediatekPkgTests
{
    [Test]
    public void StandardKey_Detects100()
    {
        var key = new byte[16];
        Random.Shared.NextBytes(key);
        var iv = new byte[16];
        Random.Shared.NextBytes(iv);
        var plain = Zeros(Unsafe.SizeOf<PkgHeader>());
        WriteAscii(plain, 0, "MTK!");
        WriteAscii(plain, 4, PkgHeader.MTK_MAGIC);
        WriteAscii(plain, 12, "1.0");
        WriteAscii(plain, 80, "test-product");
        var cipher = AesCbcEncryptNone(plain, key, iv);
        var bundle = new KeyBundle(new KeyDTO[]
        {
            new Aes128Cbc
            {
                Id = "mtkpkg-header-key",
                KeyMaterial = Convert.ToHexString(key),
                KeyIV = Convert.ToHexString(iv),
            },
        });
        var det = new MediatekPkgDetector(new KeysRepository(bundle));
        var res = (MediatekPkgDetectionResult)det.Detect(Mem(cipher));
        Assert.AreEqual(100, res.Confidence);
        Assert.NotNull(res.Context);
        Assert.AreEqual(MtkPkgVariant.Standard, res.Context!.Variant);
    }

    [Test]
    public void Random_Detects0Gracefully()
    {
        var det = new MediatekPkgDetector(EmptyKeys());
        Assert.DoesNotThrow(() =>
        {
            var res = det.Detect(Mem(Zeros(0x200)));
            Assert.AreEqual(0, res.Confidence);
        });
    }
}
