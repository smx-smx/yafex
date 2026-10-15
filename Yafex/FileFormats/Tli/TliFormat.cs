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
using System.Text;

using Yafex.FileFormats.Tar;

namespace Yafex.FileFormats.Tli;

/// <summary>
/// TLI firmware update container (Loewe TV software-update images, ".tli").
/// Layout: a short descriptor header followed by a standard ustar archive
/// holding one entry per firmware image, then zero padding.
/// Header: [tableId:8][length:12][descriptor records...][crc32-be:32],
/// where length covers the header bytes and the CRC stored at
/// [length-1..length+2] spans the preceding bytes (IEEE CRC32).
/// Only plaintext headers are recognized.
/// </summary>
public static class TliParser
{
    public const byte TableId = 0x4B;
    public const int MaxHeaderLength = 65535;

    /// <summary>Validate the header at offset; returns its length on success.</summary>
    public static bool TryParseHeader(byte[] image, long offset, out int headerLength)
    {
        headerLength = 0;
        if (offset < 0 || offset + 12 > image.Length)
        {
            return false;
        }
        if (image[offset] != TableId)
        {
            return false;
        }
        int length = ((image[offset + 1] & 0x0F) << 8) | image[offset + 2];
        if (length <= 8 || length > MaxHeaderLength)
        {
            return false;
        }
        if (offset + length + 3 > image.Length)
        {
            return false;
        }
        uint stored = (uint)((image[offset + length - 1] << 24)
            | (image[offset + length] << 16)
            | (image[offset + length + 1] << 8)
            | image[offset + length + 2]);
        if (stored == 0)
        {
            return false;
        }
        if (Support.Crc32.Compute(image, (int)offset, length - 1) != stored)
        {
            return false;
        }
        headerLength = length;
        return true;
    }

    /// <summary>Locate the tar archive following the header.</summary>
    public static bool TryFindTar(byte[] image, long headerEnd, out long tarStart)
    {
        tarStart = 0;
        long limit = Math.Min(image.Length - 512, headerEnd + 65536);
        for (long pos = headerEnd; pos < limit; pos++)
        {
            if (YafexTarReader.IsHeader(image, (int)pos))
            {
                tarStart = pos;
                return true;
            }
        }
        return false;
    }

    /// <summary>Open the tar archive at an absolute image offset.</summary>
    public static YafexTarReader OpenTar(byte[] image, long tarStart)
    {
        var stream = new MemoryStream(image, (int)tarStart, image.Length - (int)tarStart, false);
        return new YafexTarReader(stream);
    }

    public static int CountEntries(YafexTarReader tar)
    {
        int count = 0;
        foreach (var _ in tar.Entries)
        {
            count++;
        }
        return count;
    }
}

public record TliDetectionResult(int Confidence, byte[]? Image, long Offset, int HeaderLength, long TarStart, List<byte[]> Passwords) : DetectionResult(Confidence);

public class TliDetector : IFormatDetector
{
    private readonly KeysRepository _keys;
    private readonly IDictionary<string, string> _args;

    public TliDetector(KeysRepository keys, IDictionary<string, string> args)
    {
        _keys = keys;
        _args = args;
    }

    public static List<byte[]> ResolvePasswords(IDictionary<string, string> args, KeysRepository keys)
    {
        var passwords = new List<byte[]>();
        if (args.TryGetValue("password", out var text) && text.Length > 0)
        {
            passwords.Add(Encoding.ASCII.GetBytes(text));
        }
        if (keys.TryGetRaw(TliCrypto.PasswordId, out byte[] raw, out _) && raw.Length > 0)
        {
            passwords.Add(raw);
        }
        return passwords;
    }

    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 512)
            {
                return DetectionResult.Fail;
            }
            var bytes = data.ToArray();
            int found = 0;
            for (long off = 0; off + 12 <= bytes.Length && found < 64; off++)
            {
                if (bytes[off] != TliParser.TableId)
                {
                    continue;
                }
                found++;
                if (!TliParser.TryParseHeader(bytes, off, out int headerLength))
                {
                    continue;
                }
                if (!TliParser.TryFindTar(bytes, off + headerLength + 3, out long tarStart))
                {
                    continue;
                }
                int members;
                try
                {
                    members = TliParser.CountEntries(TliParser.OpenTar(bytes, tarStart));
                }
                catch (Exception)
                {
                    continue;
                }
                if (members == 0)
                {
                    continue;
                }
                return new TliDetectionResult(90, bytes, off, headerLength, tarStart,
                    ResolvePasswords(_args, _keys));
            }
            return DetectionResult.Fail;
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class TliExtractor : IFormatExtractor
{
    private readonly byte[] _image;
    private readonly long _tarStart;
    private readonly List<byte[]> _passwords;

    public TliExtractor(TliDetectionResult result)
    {
        _image = result.Image!;
        _tarStart = result.TarStart;
        _passwords = result.Passwords;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var tar = TliParser.OpenTar(_image, _tarStart);
        foreach (var e in tar.Entries)
        {
            string flat = Support.PathNames.Flatten(e.Name);
            if (flat.Length == 0 || e.Length > int.MaxValue)
            {
                continue;
            }
            var content = new byte[(int)e.Length];
            using (var s = tar.OpenFile(e.Name))
            {
                int done = 0;
                while (done < content.Length)
                {
                    int n = s.Read(content, done, content.Length - done);
                    if (n <= 0)
                    {
                        break;
                    }
                    done += n;
                }
                if (done != content.Length)
                {
                    continue;
                }
            }
            // encrypted members decrypt in place when a password works;
            // otherwise (or without keys) the raw bytes pass through
            if (TliCrypto.IsEncrypted(content)
                && TliCrypto.TryDecrypt(content, _passwords, out byte[] plain))
            {
                content = plain;
            }
            yield return ArtifactBuilder.Flat(source, flat, content);
        }
    }
}
