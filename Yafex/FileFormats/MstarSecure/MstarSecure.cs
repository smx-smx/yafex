#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
using System;
using System.IO;
using System.Security.Cryptography;

namespace Yafex.FileFormats.MstarSecure;

/// <summary>
/// MStar secure upgrade container (mstar_secure_new family): a chunk table
/// at 0x500 describes AES-CBC segments (zero IV for the first, chained
/// 16-byte tails after) covering the payload at <see cref="Chunk.DataOffset"/>.
/// Layout mirrors the reference `_dec.py` unpacker.
/// </summary>
public static class MstarSecure
{
    public const long TableOffset = 0x500;
    public const int TableSize = 128;

    public static readonly byte[] HeadMagic = "MTK....."u8.ToArray();
    public static readonly byte[] TailMagic = ".....mtk"u8.ToArray();

    /// <summary>Bundle id of the AES upgrade keys (staging secrets).</summary>
    public const string KeysId = "MSTAR_SECURE";

    public sealed class Chunk
    {
        public uint SegmentSize;
        public uint DataOffset;
        public uint DataLength;
    }

    private static uint ReadU32LE(byte[] buf, int off)
    {
        return (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));
    }

    private static bool MagicOk(byte[] buf, int off, byte[] magic)
    {
        if (off < 0 || off + magic.Length > buf.Length) return false;
        for (int i = 0; i < magic.Length; i++)
        {
            if (buf[off + i] != magic[i]) return false;
        }
        return true;
    }

    /// <summary>Parses and sanity-checks the chunk table; null when absent
    /// or implausible for an image of <paramref name="totalLength"/>.</summary>
    public static Chunk? ReadChunk(byte[] image, long totalLength)
    {
        if (totalLength < TableOffset + TableSize) return null;
        // Caller passes a head window; demand the full table.
        if (image.Length < TableOffset + TableSize) return null;
        int at = (int)TableOffset;
        if (!MagicOk(image, at, HeadMagic)) return null;
        if (!MagicOk(image, at + TableSize - 8, TailMagic)) return null;
        var chunk = new Chunk
        {
            SegmentSize = ReadU32LE(image, at + 8),
            DataOffset = ReadU32LE(image, at + 12),
            DataLength = ReadU32LE(image, at + 16),
        };
        if (chunk.SegmentSize == 0 || chunk.SegmentSize % 16 != 0
            || chunk.SegmentSize > 1 << 28) return null;
        if (chunk.DataLength == 0 || chunk.DataOffset < TableOffset + TableSize) return null;
        if (chunk.DataOffset >= totalLength) return null;
        // Last segment may be short; allow one segment of slack.
        if ((long)chunk.DataOffset + chunk.DataLength > totalLength + chunk.SegmentSize) return null;
        return chunk;
    }

    public static long SegmentCount(Chunk chunk)
    {
        return ((long)chunk.DataLength + chunk.SegmentSize - 1) / chunk.SegmentSize;
    }

    /// <summary>Decrypts one raw on-disk segment (<paramref name="cipher"/> is
    /// exactly the segment bytes; <paramref name="prevTail"/> the 16
    /// ciphertext bytes before them, null for the first segment whose IV is
    /// zero). The tail need not fill the segment; it must be block-aligned.
    /// Returns the raw decrypted bytes (caller trims the last segment).</summary>
    public static byte[] DecryptSegment(byte[] cipher, long index, byte[] key, byte[]? prevTail)
    {
        if (cipher.Length == 0) throw new InvalidDataException("MSTAR_SECURE segment out of range");
        // Cipher needs whole blocks; the on-disk tail is block-aligned on
        // observed images, fail loudly otherwise instead of guessing.
        if (cipher.Length % 16 != 0) throw new InvalidDataException("MSTAR_SECURE trailing segment not block-aligned");
        byte[] ivec = new byte[16];
        if (index > 0)
        {
            if (prevTail == null || prevTail.Length != 16)
            {
                throw new InvalidDataException("MSTAR_SECURE segment missing IV tail");
            }
            ivec = prevTail;
        }
        using var aes = Aes.Create();
        aes.BlockSize = 128;
        aes.KeySize = key.Length * 8;
        aes.Mode = CipherMode.CBC;
        aes.IV = ivec;
        aes.Key = key;
        aes.Padding = PaddingMode.None;
        return aes.CreateDecryptor().TransformFinalBlock(cipher, 0, cipher.Length);
    }

    /// <summary>Reads one raw on-disk segment plus its IV tail.</summary>
    public static (byte[] Cipher, byte[]? PrevTail) ReadSegment(byte[] image, Chunk chunk, long index)
    {
        long off = chunk.DataOffset + index * chunk.SegmentSize;
        int take = (int)Math.Min(chunk.SegmentSize, image.Length - off);
        if (take <= 0) throw new InvalidDataException("MSTAR_SECURE segment out of range");
        var cipher = new byte[take];
        Array.Copy(image, off, cipher, 0, take);
        byte[]? prevTail = null;
        if (index > 0)
        {
            if (off - 16 < 0) throw new InvalidDataException("MSTAR_SECURE first segment misaligned");
            prevTail = new byte[16];
            Array.Copy(image, off - 16, prevTail, 0, 16);
        }
        return (cipher, prevTail);
    }

    /// <summary>Full decrypted payload (DataLength bytes).</summary>
    public static byte[] DecryptAll(byte[] image, Chunk chunk, byte[] key)
    {
        var output = new byte[chunk.DataLength];
        long done = 0;
        long count = SegmentCount(chunk);
        for (long i = 0; i < count; i++)
        {
            var (cipher, prevTail) = ReadSegment(image, chunk, i);
            var plain = DecryptSegment(cipher, i, key, prevTail);
            long want = Math.Min(plain.Length, (long)chunk.DataLength - done);
            Array.Copy(plain, 0, output, done, want);
            done += want;
        }
        return output;
    }

    /// <summary>First 80 payload bytes decrypted (zero IV); callers check
    /// for the expected script text. Null when out of range.</summary>
    public static byte[]? DecryptProbe(byte[] image, Chunk chunk, byte[] key)
    {
        const int want = 80;
        if ((long)chunk.DataOffset + want > image.Length) return null;
        var cipher = new byte[want];
        Array.Copy(image, chunk.DataOffset, cipher, 0, want);
        using var aes = Aes.Create();
        aes.BlockSize = 128;
        aes.KeySize = key.Length * 8;
        aes.Mode = CipherMode.CBC;
        aes.IV = new byte[16];
        aes.Key = key;
        aes.Padding = PaddingMode.None;
        return aes.CreateDecryptor().TransformFinalBlock(cipher, 0, cipher.Length);
    }

    /// <summary>Inner payloads are mboot scripts: plain ASCII text.</summary>
    public static bool IsScriptText(byte[] probe)
    {
        if (probe.Length == 0) return false;
        foreach (byte b in probe)
        {
            if (b != 0x09 && b != 0x0A && b != 0x0D && (b < 0x20 || b > 0x7E))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>First collection key whose decryption yields script text;
    /// null when none validates (wrong or missing keys).</summary>
    public static byte[]? FindValidKey(
        byte[] image, Chunk chunk, System.Collections.Generic.IEnumerable<KeyEntry> keys)
    {
        foreach (var entry in keys)
        {
            if (entry.key.Length != 16 && entry.key.Length != 24 && entry.key.Length != 32)
            {
                continue;
            }
            try
            {
                var probe = DecryptProbe(image, chunk, entry.key);
                // Some images salt the first block with random bytes (their
                // script even says so); accept text at either window.
                if (probe != null && (IsScriptText(probe[..64])
                    || IsScriptText(probe[16..80])))
                {
                    return entry.key;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException
                || ex is System.Security.Cryptography.CryptographicException
                || ex is ArgumentException)
            {
                continue;
            }
        }
        return null;
    }

    /// <summary>Reconstructed inner image: stored header plus decrypted payload.</summary>
    public static byte[] DecryptImage(byte[] image, Chunk chunk, byte[] key)
    {
        var payload = DecryptAll(image, chunk, key);
        var output = new byte[chunk.DataOffset + payload.Length];
        Array.Copy(image, 0, output, 0, (int)Math.Min(chunk.DataOffset, image.Length));
        Array.Copy(payload, 0, output, chunk.DataOffset, payload.Length);
        return output;
    }
}
