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
// Synthetic MTK PKG old cipher tests (test vector encrypted with an
// independent Feistel implementation). Fully in-memory.
using System;

using NUnit.Framework;

using Yafex.FileFormats.MediatekPkgOld;

namespace UnitTests;

[TestFixture]
public class MediatekPkgOldCryptoTests
{
    private static readonly byte[] Key = Convert.FromHexString("09291094");
    private static readonly byte[] Iv = Convert.FromHexString("757CBE04");

    // plaintext: "TPV\0" + "#DH@FiRm" + "2.25" + 0x10..0x1F
    private const string PlainHex = "54505600234448404669526d322e3235101112131415161718191a1b1c1d1e1f";
    private const string CipherHex = "875e648730c0d2c54b87d45536cc0aa3345e9012c3271dfd9f4cc7c5c5a85a00";

    [Test]
    public void Decrypt_KnownVector_MatchesPlaintext()
    {
        var back = MtkOldCrypto.Decrypt(Convert.FromHexString(CipherHex), Key, Iv);
        CollectionAssert.AreEqual(Convert.FromHexString(PlainHex), back);
    }

    [Test]
    public void Decrypt_RejectsBadKeySizes()
    {
        Assert.Throws<ArgumentException>(() => MtkOldCrypto.Decrypt(new byte[8], new byte[3], Iv));
        Assert.Throws<ArgumentException>(() => MtkOldCrypto.Decrypt(new byte[8], Key, new byte[5]));
    }

    [Test]
    public void Decrypt_ShortInput_DoesNotThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            var back = MtkOldCrypto.Decrypt(new byte[] { 1, 2 }, Key, Iv);
            Assert.AreEqual(2, back.Length);
        });
    }
}
