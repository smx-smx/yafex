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
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

using Smx.SharpIO.Memory.Buffers;

using Yafex.FileFormats.MediatekPkg;
using Yafex.FileFormats.MediatekPkgOld;
using Yafex.Support;

namespace Yafex.Fuse;

/// <summary>Per-mount shared MTK key state: the data AES key is searched once
/// (trial-decrypt of a 64-byte window) and memoized for all parts.</summary>
public sealed class MtkKeyState
{
    public KeysRepository Keys;
    public byte[]? VendorMagic;
    public Aes? DataAes;
    public bool Searched;
    public readonly object Gate = new();

    public MtkKeyState(KeysRepository keys, byte[]? vendorMagic = null)
    {
        Keys = keys;
        VendorMagic = vendorMagic;
    }
}

/// <summary>Lazy MTK part payload as a virtual file. Mount only walks part
/// entries (plaintext structs); keys, meta parsing and payload bytes all
/// resolve on first access. Encrypted new-variant parts stream AES windows;
/// old-variant parts memoize the decrypted region on first open (loaders
/// are small); keyless encrypted parts serve stored bytes like the
/// extractor's fallback.</summary>
public sealed class MtkPartFile : IVirtualFile
{
    private static readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof(MtkPartFile));

    private const string DataHeaderMagic = "reserved mtk inc";
    private const string MetaMagic = "iMtK";

    private readonly IDataSource _input;
    private readonly long _regionStart;
    private readonly long _regionLength;
    private readonly bool _encrypted;
    private readonly bool _oldVariant;
    private readonly MtkKeyState _ks;
    private readonly object _gate = new();

    private bool _ready;
    private long _payloadSkip;
    private long _length;
    private byte[]? _key;
    private byte[]? _iv;
    private byte[]? _memoRegion;
    private bool _warned;

    public MtkPartFile(string name, IDataSource input, long regionStart, long regionLength,
        bool encrypted, bool oldVariant, MtkKeyState keyState)
    {
        Name = name;
        _input = input;
        _regionStart = regionStart;
        _regionLength = regionLength;
        _encrypted = encrypted;
        _oldVariant = oldVariant;
        _ks = keyState;
    }

    public string Name { get; }

    public long Length
    {
        get
        {
            EnsureResolved();
            return _length;
        }
    }

    private byte[] ReadRaw(long offset, int count)
    {
        return _input.Data.Slice(_regionStart + offset, count).ToArray();
    }

    private void EnsureKey()
    {
        if (_ks.DataAes != null || _ks.Searched) return;
        lock (_ks.Gate)
        {
            if (_ks.DataAes != null || _ks.Searched) return;
            _ks.Searched = true;
            // trial-decrypt a small window with every collection key
            // (block-aligned: TransformFinalBlock needs full blocks)
            var probeLen = ((int)Math.Min(64, _regionLength) / 16) * 16;
            if (probeLen <= 0) return;
            var sample = ReadRaw(0, probeLen);
            foreach (var entry in CollectionEntries())
            {
                try
                {
                    using var aes = entry.GetAes();
                    using var dec = aes.CreateDecryptor();
                    var pt = dec.TransformFinalBlock(sample, 0, sample.Length);
                    if (Encoding.ASCII.GetString(pt, 0, Math.Min(DataHeaderMagic.Length, pt.Length)) == DataHeaderMagic)
                    {
                        _ks.DataAes = Aes.Create();
                        _ks.DataAes.Key = (byte[])entry.key.Clone();
                        _ks.DataAes.Mode = entry.keyMode;
                        _ks.DataAes.Padding = PaddingMode.None;
                        if (entry.keyMode == CipherMode.CBC)
                        {
                            _ks.DataAes.IV = (byte[])entry.iv.Clone();
                        }
                        return;
                    }
                }
                catch (Exception) { }
            }
            // vendor-magic fallback mirrors the extractor
            if (_ks.VendorMagic != null)
            {
                var vk = new byte[16];
                for (int i = 0; i < 16; i++) vk[i] = _ks.VendorMagic[i % _ks.VendorMagic.Length];
                try
                {
                    using var aes = Aes.Create();
                    aes.Key = vk;
                    aes.IV = new byte[16];
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.None;
                    using var dec = aes.CreateDecryptor();
                    var pt = dec.TransformFinalBlock(sample, 0, sample.Length);
                    if (Encoding.ASCII.GetString(pt, 0, Math.Min(DataHeaderMagic.Length, pt.Length)) == DataHeaderMagic)
                    {
                        _ks.DataAes = Aes.Create();
                        _ks.DataAes.Key = vk;
                        _ks.DataAes.IV = new byte[16];
                        _ks.DataAes.Mode = CipherMode.CBC;
                        _ks.DataAes.Padding = PaddingMode.None;
                    }
                }
                catch (Exception) { }
            }
        }
    }

    private IEnumerable<KeyEntry> CollectionEntries()
    {
        if (_ks.Keys.TryGetKeyCollection("mtkpkg-keys", out var entries))
        {
            foreach (var e in entries) yield return e;
        }
    }

    private void EnsureResolved()
    {
        if (_ready) return;
        lock (_gate)
        {
            if (_ready) return;
            int headLen = _oldVariant ? 0 : 48;
            long metaLen = 0;
            _key = null;
            _iv = null;
            if (_encrypted)
            {
                if (_oldVariant)
                {
                    // vendor key known upfront; IV from bundle
                    _key = _ks.VendorMagic;
                    if (_ks.Keys.TryGetRaw("mtkpkg-old-data-iv", out _, out var div) && div.Length == 4)
                    {
                        _iv = div;
                    }
                }
                else
                {
                    EnsureKey();
                    if (_ks.DataAes != null)
                    {
                        _key = _ks.DataAes.Key;
                        _iv = _ks.DataAes.IV;
                    }
                }
            }
            if (_key != null)
            {
                // decrypt just enough to parse the metadata prefix; any
                // short/corrupt region falls back to raw mode below.
                // Only the 8-byte iMtK header is needed (the length word);
                // meta content itself is never read at resolve time.
                try
                {
                    var probe = DecryptWindowLocal(0, (int)Math.Min(headLen + 8, _regionLength));
                    int off = headLen;
                    if (probe.Length >= off + 8
                        && Encoding.ASCII.GetString(probe, off, 4) == MetaMagic)
                    {
                        int mlen = BitConverter.ToInt32(probe, off + 4);
                        if (mlen >= 0 && mlen <= (1 << 20) && (long)off + 8 + mlen <= _regionLength)
                        {
                            metaLen = 8 + mlen;
                        }
                    }
                }
                catch (Exception ex) when (ex is ArgumentOutOfRangeException || ex is CryptographicException || ex is ArgumentException)
                {
                    _key = null;
                }
            }
            else if (!_encrypted)
            {
                var probe = ReadRaw(0, (int)Math.Min(headLen + 8, _regionLength));
                int off = headLen;
                if (probe.Length >= off + 8
                    && Encoding.ASCII.GetString(probe, off, 4) == MetaMagic)
                {
                    int mlen = BitConverter.ToInt32(probe, off + 4);
                    if (mlen >= 0 && mlen <= (1 << 20) && (long)off + 8 + mlen <= _regionLength)
                    {
                        metaLen = 8 + mlen;
                    }
                }
            }
            _payloadSkip = headLen + metaLen;
            if (_encrypted && _key == null)
            {
                // raw fallback serves the whole stored region
                _payloadSkip = 0;
                _length = _regionLength;
            }
            else
            {
                _length = Math.Max(0, _regionLength - _payloadSkip);
            }
            _ready = true;
        }
    }

    private byte[] DecryptWindowLocal(long offset, int count)
    {
        // single-shot decrypt used only for tiny resolve windows (offset is
        // always 0 here). Old variant: decrypt a prefix; CBC-from-start plus
        // the IV rule make [0..n) exact, tail-xor fallout stays past it.
        if (_oldVariant)
        {
            int want = (int)Math.Min(_regionLength, offset + count);
            want = ((want + 3) / 4) * 4;
            if (want > _regionLength) return [];
            var raw = ReadRaw(0, want);
            return MtkOldCrypto.Decrypt(raw, _key!, _iv!)[(int)offset..((int)offset + count)];
        }
        var aes = Aes.Create();
        aes.Key = _key!;
        aes.IV = _iv!;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var dec = aes.CreateDecryptor();
        long firstBlock = offset / 16;
        long nBlocks = ((offset + count - 1) / 16) - firstBlock + 1;
        var cipher = ReadRaw(firstBlock * 16, (int)(nBlocks * 16));
        var pt = dec.TransformFinalBlock(cipher, 0, cipher.Length);
        var outBuf = new byte[count];
        Array.Copy(pt, (int)(offset - firstBlock * 16), outBuf, 0, count);
        return outBuf;
    }

    public Stream OpenRead()
    {
        EnsureResolved();
        if (_key == null)
        {
            if (_encrypted && !_warned)
            {
                _warned = true;
                log.WarnFormat("part '{0}': no key, serving stored bytes", Name);
            }
            return new DataSourceRangeStream(_input, _regionStart + _payloadSkip, _length);
        }
        if (_oldVariant)
        {
            if (_memoRegion == null)
            {
                lock (_gate)
                {
                    _memoRegion ??= MtkOldCrypto.Decrypt(ReadRaw(0, (int)_regionLength), _key, _iv!);
                }
            }
            var payload = new byte[_length];
            Array.Copy(_memoRegion, _payloadSkip, payload, 0, _length);
            return new MemoryStream(payload, writable: false);
        }
        return new AesCbcDecryptStream(_input, _regionStart, _regionLength,
            _payloadSkip, _length, _key, _iv!, _key);
    }
}

/// <summary>Shared lazy MTK/MTK-old mount builder: walks part entries
/// (plaintext structs only, no payload IO at mount) and mounts per-part
/// decrypted/raw/meta/expanded views. Returns null when the walk fails so
/// callers fall back gracefully.</summary>
public static class MtkVfs
{
    public static YafexVfs? CreateNew(MFile input, KeysRepository keys, MediatekPkgDetectionResult result)
    {
        var ctx = result.Context;
        if (ctx == null) return null;
        try
        {
            var hdr = ctx.Header.Span[0];
            long pos = Unsafe.SizeOf<PkgHeader>()
                + (ctx.Variant == MtkPkgVariant.Standard ? PkgHeader.STANDARD_DIGEST_SIZE : PkgHeader.NEW_DIGEST_SIZE)
                + (ctx.Flags.HasFlag(MtkPkgQuirks.Philips) ? 0x80 : 0);
            var vfs = new YafexVfs();
            var rootDir = new YafexDirectory(input.Name ?? "mtk", Helpers.OctalLiteral(0755));
            vfs.Root.AddNode(rootDir);
            var keepAlive = new List<object>();
            var ks = new MtkKeyState(keys, vendorMagic: [.. hdr.VendorMagicBytes]);
            var data = input.Data;
            int count = 0;
            while (pos < data.Length)
            {
                if (ctx.Flags.HasFlag(MtkPkgQuirks.Philips) && pos == data.Length - 0x100) break;
                if (data.Length - pos < Unsafe.SizeOf<PartEntry>()) break;
                var part = data.Slice(pos, Unsafe.SizeOf<PartEntry>()).ReadStruct<PartEntry>();
                long regionStart = pos + 12;
                long regionLen = (long)part.Size + 48;
                if (regionLen > data.Length - regionStart) break;
                AddPart(rootDir, input, $"{part.PartName}.pak", regionStart, regionLen,
                    part.Flags.HasFlag(PartFlags.Encrypted), oldVariant: false, ks, keepAlive,
                    $"offset: 0x{regionStart:X}\nsize: {part.Size}\nflags: {part.Flags}");
                pos = regionStart + regionLen;
                if (++count > 100000) break;
            }
            return vfs;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static YafexVfs? CreateOld(MFile input, KeysRepository keys, MediatekPkgOldDetectionResult result)
    {
        var ctx = result.Context;
        if (ctx == null) return null;
        try
        {
            var hdr = ctx.Header;
            long pos = ctx.HeaderOffset + MtkOldHeader.SIZE;
            var vfs = new YafexVfs();
            var rootDir = new YafexDirectory(input.Name ?? "mtk", Helpers.OctalLiteral(0755));
            vfs.Root.AddNode(rootDir);
            var keepAlive = new List<object>();
            var ks = new MtkKeyState(keys, vendorMagic: hdr.VendorMagicBytes);
            var data = input.Data;
            int count = 0;
            while (pos < data.Length)
            {
                if (data.Length - pos < Unsafe.SizeOf<PartEntry>()) break;
                var part = data.Slice(pos, Unsafe.SizeOf<PartEntry>()).ReadStruct<PartEntry>();
                long regionStart = pos + 12;
                long regionLen = part.Size;
                if (regionLen > data.Length - regionStart) break;
                AddPart(rootDir, input, $"{part.PartName}.pak", regionStart, regionLen,
                    part.Flags.HasFlag(PartFlags.Encrypted), oldVariant: true, ks, keepAlive,
                    $"offset: 0x{regionStart:X}\nsize: {part.Size}\nflags: {part.Flags}");
                pos = regionStart + regionLen;
                if (++count > 100000) break;
            }
            return vfs;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void AddPart(YafexDirectory rootDir, MFile input, string name,
        long regionStart, long regionLength, bool encrypted, bool oldVariant,
        MtkKeyState ks, List<object> keepAlive, string meta)
    {
        var decrypted = new MtkPartFile(name, input, regionStart, regionLength, encrypted, oldVariant, ks);
        var raw = new RangeVirtualFile(name, input, regionStart, regionLength);
        VfsBuilder.AddImage(rootDir, decrypted, VfsImageViews.All, keepAlive,
            raw: raw, describe: () => meta);
    }
}
