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
// Synthetic MStarSb tests. Fully in-memory: no disk, no samples.
// RSA vectors use a generated key (BouncyCastle).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

using Yafex;
using Yafex.FileFormats.MstarSb;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class MstarSbTests
{
    private static void PutU32(List<byte> b, uint v) => b.AddRange(BitConverter.GetBytes(v));

    internal static byte[] BuildImage()
    {
        var img = new List<byte>();
        img.AddRange(new byte[0x100]);
        img.AddRange(Encoding.ASCII.GetBytes("SECURITY"));
        PutU32(img, 1);
        PutU32(img, 16);
        img.AddRange(Encoding.ASCII.GetBytes("PAYLOAD-ONE-......"));
        while (img.Count % 16 != 0) img.Add(0);
        img.AddRange(Encoding.ASCII.GetBytes("SECURITY"));
        PutU32(img, 1);
        PutU32(img, 16);
        img.AddRange(Encoding.ASCII.GetBytes("PAYLOAD-TWO-......"));
        return img.ToArray();
    }

    [Test]
    public void Headers_Detects80()
    {
        var res = (MstarSbDetectionResult)new MstarSbAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage()));
        Assert.AreEqual(80, res.Confidence);
        Assert.AreEqual(2, res.Headers);
    }

    [Test]
    public void Extract_SplitsAtHeaders()
    {
        var mem = Mem(BuildImage(), "tzfw.pak");
        var det = (MstarSbDetectionResult)new MstarSbAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new MstarSbAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(2, artifacts.Count);
        Assert.AreEqual("sec_100.bin", artifacts[0].Name);
        Assert.IsTrue(artifacts[0].Data.ToArray().Length > 16);
    }

    [Test]
    public void Garbage_Detects0()
    {
        var res = new MstarSbAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(Encoding.ASCII.GetBytes("definitely not secure boot..........")));
        Assert.AreEqual(0, res.Confidence);
    }

    private static AsymmetricCipherKeyPair GenKey()
    {
        var gen = new RsaKeyPairGenerator();
        gen.Init(new KeyGenerationParameters(new SecureRandom(), 1024));
        return gen.GenerateKeyPair();
    }

    [Test]
    public void Rsa_RoundtripPasses()
    {
        var kp = GenKey();
        var pub = (RsaKeyParameters)kp.Public;
        var data = Encoding.ASCII.GetBytes("fragment data for verification");
        var signer = SignerUtilities.GetSigner("SHA256withRSA");
        signer.Init(true, kp.Private);
        signer.BlockUpdate(data, 0, data.Length);
        byte[] sig = signer.GenerateSignature();
        byte[] n = pub.Modulus.ToByteArrayUnsigned();
        byte[] e = pub.Exponent.ToByteArrayUnsigned();
        // left-pad to fixed widths like on-disk keyslots
        var nPad = new byte[128];
        var ePad = new byte[4];
        Array.Copy(n, 0, nPad, 128 - n.Length, n.Length);
        Array.Copy(e, 0, ePad, 4 - e.Length, e.Length);
        Assert.IsTrue(MstarRsa.Verify(nPad, ePad, data, sig));
    }

    [Test]
    public void Rsa_TamperedFails()
    {
        var kp = GenKey();
        var pub = (RsaKeyParameters)kp.Public;
        var data = Encoding.ASCII.GetBytes("fragment data for verification");
        var signer = SignerUtilities.GetSigner("SHA256withRSA");
        signer.Init(true, kp.Private);
        signer.BlockUpdate(data, 0, data.Length);
        byte[] sig = signer.GenerateSignature();
        sig[10] ^= 0xFF;
        byte[] n = pub.Modulus.ToByteArrayUnsigned();
        byte[] e = pub.Exponent.ToByteArrayUnsigned();
        var nPad = new byte[128];
        var ePad = new byte[4];
        Array.Copy(n, 0, nPad, 128 - n.Length, n.Length);
        Array.Copy(e, 0, ePad, 4 - e.Length, e.Length);
        Assert.IsFalse(MstarRsa.Verify(nPad, ePad, data, sig));
    }

    [Test]
    public void Stripe_ReassemblesContiguous()
    {
        var img = BuildImage();
        var hdr = MstarSb.FindHeaders(img)[0];
        var stripe = MstarSb.ReassembleStripe(img, img.Length, hdr, 0, 4);
        Assert.AreEqual(4 * 16, stripe.Length);
    }
}
