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
// Synthetic MSDU11 tests: a minimal container (2 sections, 1 header entry,
// TOC encrypted with a known passphrase, one AES member + one plain member)
// is built in-memory; detection, TOC mapping and member decrypts run
// without fixtures. Fully in-memory.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Msd11;

namespace UnitTests;

[TestFixture]
public class Msd11Tests
{
    private static readonly byte[] Passphrase =
        Convert.FromHexString("00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff");

    private static readonly byte[] PlainMember = Encoding.ASCII.GetBytes("plain-member-data!");
    private static readonly byte[] SecretMember = Encoding.ASCII.GetBytes("secret-member-data!!");

    private static byte[] SaltedEncrypt(byte[] plain, byte[] passphrase, byte[] salt)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.KeySize = passphrase.Length * 8;
        aes.Key = passphrase;
        aes.IV = SHA256.HashData(salt)[..16];
        using var enc = aes.CreateEncryptor();
        var body = enc.TransformFinalBlock(plain, 0, plain.Length);
        var blob = new byte[16 + body.Length];
        Encoding.ASCII.GetBytes("Salted__").CopyTo(blob, 0);
        Array.Copy(salt, 0, blob, 8, 8);
        Array.Copy(body, 0, blob, 16, body.Length);
        return blob;
    }

    private static void PutU16(List<byte> b, ushort v) => b.AddRange(BitConverter.GetBytes(v));
    private static void PutU32(List<byte> b, uint v) => b.AddRange(BitConverter.GetBytes(v));
    private static void PutU64(List<byte> b, ulong v) => b.AddRange(BitConverter.GetBytes(v));

    private static byte[] BuildToc()
    {
        var b = new List<byte>();
        PutU32(b, 7); // public key id
        PutU16(b, 256); // RSA2048 signature size (magic must land at +262)
        b.AddRange(new byte[256]);
        b.AddRange(Encoding.ASCII.GetBytes(Msd11Toc.TreeMagic + "\0"));
        PutU32(b, 3); // top-level descriptors

        // Item 1001 "a.bin" with AES salt
        var salt = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 };
        var item = new List<byte>();
        item.Add(0); // flag
        PutU32(item, 1001);
        item.Add(0); // unk
        PutU64(item, (ulong)SecretMember.Length);
        PutU64(item, (ulong)SecretMember.Length + 16);
        PutU16(item, 0);
        PutU32(item, 2); // subdescriptor count
        // 0x0A name
        item.Add(0); PutU32(item, 4 + 1 + 1 + 5); PutU32(item, 0x0A);
        item.Add(0); item.Add(5); item.AddRange(Encoding.ASCII.GetBytes("a.bin"));
        // 0x07 processing -> 0x0E aes
        var aes = new List<byte>();
        aes.Add(0); PutU32(aes, (uint)(4 + 1 + 1 + salt.Length + 8)); PutU32(aes, 0x0E);
        aes.Add(0); aes.Add((byte)salt.Length); aes.AddRange(salt);
        PutU64(aes, (ulong)SecretMember.Length + 16);
        item.Add(0); PutU32(item, 4u + 1u + 4u + (uint)aes.Count); PutU32(item, 0x07);
        item.Add(0); PutU32(item, 1);
        item.AddRange(aes);
        b.Add(0); PutU32(b, 4u + (uint)item.Count); PutU32(b, 0x01);
        b.AddRange(item);

        // Item 1002 "b.bin", stored plain (name only, no processing block)
        var item2 = new List<byte>();
        item2.Add(0); PutU32(item2, 1002);
        item2.Add(0);
        PutU64(item2, (ulong)PlainMember.Length);
        PutU64(item2, (ulong)PlainMember.Length);
        PutU16(item2, 0);
        PutU32(item2, 1); // subdescriptor count
        item2.Add(0); PutU32(item2, 4 + 1 + 1 + 5); PutU32(item2, 0x0A);
        item2.Add(0); item2.Add(5); item2.AddRange(Encoding.ASCII.GetBytes("b.bin"));
        b.Add(0); PutU32(b, 4u + (uint)item2.Count); PutU32(b, 0x01);
        b.AddRange(item2);

        // Group with image version
        var grp = new List<byte>();
        grp.Add(0); PutU32(grp, 77); PutU32(grp, 0x02);
        grp.Add(0); PutU32(grp, 99);
        PutU32(grp, 1);
        grp.Add(0); PutU32(grp, 4 + 1 + 1 + 8 + 4); PutU32(grp, 0x19);
        grp.Add(0); grp.Add(8); grp.AddRange(Encoding.ASCII.GetBytes("TEST-FW."));
        PutU16(grp, 3); PutU16(grp, 14);
        b.AddRange(grp);
        // The OUITH validator probes magic at +518: real blobs always exceed
        // it (256-byte signature + descriptors); pad the synthetic one too.
        while (b.Count < 600) b.Add(0);
        return b.ToArray();
    }

    private static byte[] BuildImage(out byte[] memberSalt)
    {
        memberSalt = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 };
        byte[] toc = SaltedEncrypt(BuildToc(), Passphrase, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        byte[] enc = SaltedEncrypt(SecretMember, Passphrase, memberSalt);
        // Raw member form has no Salted__ framing: strip the 16-byte prefix.
        var encRaw = new byte[enc.Length - 16];
        Array.Copy(enc, 16, encRaw, 0, encRaw.Length);

        // Header-entry table: single TOC entry "TEST-FW".
        var htab = new List<byte>();
        PutU32(htab, 1); // header count
        string fw = "TEST-FW";
        long tocOff = 22 + 2 * 20 + 4 + 13 + fw.Length;
        PutU64(htab, (ulong)tocOff);
        PutU32(htab, (uint)(toc.Length + 8));
        htab.Add((byte)fw.Length);
        htab.AddRange(Encoding.ASCII.GetBytes(fw));

        long sec0Off = tocOff + toc.Length + 8;
        long sec1Off = sec0Off + encRaw.Length;

        var img = new List<byte>();
        img.AddRange(Encoding.ASCII.GetBytes("MSDU11"));
        PutU32(img, 0); // checksum (unchecked)
        PutU64(img, 216); // header size
        PutU32(img, 2); // sections
        PutU32(img, 1001); PutU64(img, (ulong)sec0Off); PutU64(img, (ulong)encRaw.Length);
        PutU32(img, 1002); PutU64(img, (ulong)sec1Off); PutU64(img, (ulong)PlainMember.Length);
        img.AddRange(htab);
        while (img.Count < tocOff) img.Add(0);
        img.AddRange(new byte[8]); // TOC +8 framing pad
        img.AddRange(toc);
        while (img.Count < sec0Off) img.Add(0);
        img.AddRange(encRaw);
        img.AddRange(PlainMember);
        return img.ToArray();
    }

    private static KeysRepository TestKeys() => new KeysRepository(new KeyBundle(new KeyDTO[]
    {
        new KeyCollectionDTO
        {
            Id = "MSD11",
            Keys = new List<KeyDTO>
            {
                // Passphrase-only entry: the IV slot is unused by MSD11
                // (the IV derives from each blob's salt).
                new Aes256Cbc
                {
                    Id = "test",
                    KeyMaterial = Convert.ToHexString(Passphrase),
                    KeyIV = new string('0', 32),
                    Description = "synthetic"
                }
            }
        }
    }));

    [Test]
    public void Detector_OpensTocWithBundleKey()
    {
        var image = BuildImage(out _);
        var source = new MemoryDataSource(image) { Name = "test.msd" };
        var detector = new Msd11Detector(TestKeys(), new Dictionary<string, string>());
        var result = (Msd11DetectionResult)detector.Detect(source);
        Assert.That(result.Confidence, Is.EqualTo(100));
        Assert.That(result.Context, Is.Not.Null);
        Assert.That(result.Context!.Members.Count, Is.EqualTo(2));
        Assert.That(result.Context.Members[0].Name, Is.EqualTo("a.bin"));
        Assert.That(result.Context.Members[0].Encrypted, Is.True);
        Assert.That(result.Context.Members[1].Name, Is.EqualTo("b.bin"));
        Assert.That(result.Context.Members[1].Encrypted, Is.False);
        Assert.That(result.Context.FirmwareName, Is.EqualTo("TEST-FW"));
        Assert.That(result.Context.Info!.Name, Is.EqualTo("TEST-FW."));
    }

    [Test]
    public void Reader_DecryptsEncryptedMember()
    {
        var image = BuildImage(out var salt);
        var ctx = Msd11Reader.Open(image, new[] { Passphrase });
        Assert.That(ctx, Is.Not.Null);
        var member = ctx!.Members[0];
        Assert.That(member.Salt, Is.EqualTo(salt));
        var plain = Msd11Reader.ReadMember(image, member, Passphrase);
        Assert.That(plain, Is.EqualTo(SecretMember));
    }

    [Test]
    public void Reader_PassesPlainMemberThrough()
    {
        var image = BuildImage(out _);
        var ctx = Msd11Reader.Open(image, new[] { Passphrase });
        Assert.That(ctx, Is.Not.Null);
        var member = ctx!.Members[1];
        Assert.That(member.Name, Is.EqualTo("b.bin"));
        Assert.That(Msd11Reader.ReadMember(image, member, Passphrase), Is.EqualTo(PlainMember));
    }


    [Test]
    public void Detector_ReportsMagicWithoutKeys()
    {
        var image = BuildImage(out _);
        var source = new MemoryDataSource(image) { Name = "test.msd" };
        var detector = new Msd11Detector(
            new KeysRepository(KeyBundle.Empty), new Dictionary<string, string>());
        var result = (Msd11DetectionResult)detector.Detect(source);
        Assert.That(result.Confidence, Is.EqualTo(30));
        Assert.That(result.Context, Is.Null);
    }
}
