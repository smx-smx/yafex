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
using System.IO;
using System.Security.Cryptography;

using Yafex.FileFormats.Msd11;
using Yafex.Support;

namespace Yafex.Fuse;

/// <summary>Stored member bytes as a virtual file (the `.raw` view).</summary>
public sealed class Msd11StoredFile : IVirtualFile
{
    private readonly IDataSource _input;
    private readonly long _offset;

    public Msd11StoredFile(string name, IDataSource input, long offset, long length)
    {
        Name = name;
        _input = input;
        _offset = offset;
        Length = length;
    }

    public string Name { get; }
    public long Length { get; }

    public Stream OpenRead() => new DataSourceRangeStream(_input, _offset, Length);
}

/// <summary>Decrypted member view: plain members are a stored slice, AES
/// members decrypt CBC windows on the fly through the shared windowed
/// AES-CBC stream (reads only touch their covering blocks).</summary>
public sealed class Msd11MemberFile : IVirtualFile
{
    private readonly IDataSource _input;
    private readonly Msd11Member _member;
    private readonly byte[] _key;
    private long? _length;

    public Msd11MemberFile(string name, IDataSource input, Msd11Member member, byte[] key)
    {
        Name = name;
        _input = input;
        _member = member;
        _key = key;
    }

    public string Name { get; }

    public long Length
    {
        get
        {
            if (_length == null)
            {
                _length = _member.Encrypted
                    ? _member.Size - PaddingOf(_input, _member, _key)
                    : _member.Size;
            }
            return _length.Value;
        }
    }

    public Stream OpenRead()
    {
        if (!_member.Encrypted)
        {
            return new DataSourceRangeStream(_input, _member.Offset, _member.Size);
        }
        if (_member.Salt == null) throw new InvalidDataException($"no salt for {Name}");
        var iv = Msd11Crypto.DeriveIv(_key, _member.Salt);
        return new AesCbcDecryptStream(
            _input, _member.Offset, _member.Size, 0, Length, _key, iv);
    }

    private static int PaddingOf(IDataSource input, Msd11Member member, byte[] key)
    {
        if (member.Salt == null) throw new InvalidDataException($"no salt for {member.Name}");
        if ((member.Size & 15) != 0 || member.Size < 16)
        {
            throw new InvalidDataException($"member {member.Name} is not block-aligned");
        }
        // PKCS7 pad lives in the last stored block; its CBC IV is the
        // previous ciphertext block (or the stream IV for one-block members).
        // Decrypt padding-less and validate the pad bytes by hand.
        var iv = Msd11Crypto.DeriveIv(key, member.Salt);
        byte[] prev = iv;
        if (member.Size > 16)
        {
            prev = input.Data.Slice(member.Offset + member.Size - 32, 16).ToArray();
        }
        var last = input.Data.Slice(member.Offset + member.Size - 16, 16).ToArray();
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = prev;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var dec = aes.CreateDecryptor();
        var plain = dec.TransformFinalBlock(last, 0, 16);
        int pad = plain[15];
        if (pad < 1 || pad > 16) throw new InvalidDataException("bad PKCS7 padding");
        for (int i = 16 - pad; i < 16; i++)
        {
            if (plain[i] != pad) throw new InvalidDataException("bad PKCS7 padding");
        }
        return pad;
    }
}

/// <summary>Lazy MSDU11 mount: members resolve from the detection-time TOC
/// (no payload IO at mount); each member mounts decrypted with stored,
/// metadata and expanded (GPT/VDFS chain) views.</summary>
public static class Msd11Vfs
{
    public static YafexVfs? Create(IDataSource input, Msd11Context ctx)
    {
        try
        {
            if (ctx.Members.Count == 0) return null;
            var vfs = new YafexVfs();
            var rootDir = new YafexDirectory(ctx.FirmwareName, Helpers.OctalLiteral(0755));
            vfs.Root.AddNode(rootDir);
            var keepAlive = new List<object>();
            foreach (var member in ctx.Members)
            {
                var name = Support.PathNames.BaseName(member.Name);
                if (string.IsNullOrEmpty(name)) name = $"id{member.Index}";
                var stored = new Msd11StoredFile(name, input, member.Offset, member.Size);
                var file = new Msd11MemberFile(name, input, member, ctx.Key);
                VfsBuilder.AddImage(rootDir, file,
                    VfsImageViews.Decrypted | VfsImageViews.Expanded | VfsImageViews.Metadata,
                    keepAlive,
                    raw: member.Encrypted ? stored : null,
                    describe: () => $"member-id: {member.Index}\nname: {member.Name}\n" +
                        $"offset: 0x{member.Offset:X}\nstored: {member.Size}\n" +
                        $"encrypted: {member.Encrypted}\n");
            }
            return vfs;
        }
        catch (Exception)
        {
            return null;
        }
    }

}
