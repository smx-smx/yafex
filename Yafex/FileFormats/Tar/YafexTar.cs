#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 *
 * Tar reader vendored from DiscUtils (Library/DiscUtils.Core/Archives:
 * TarFile.cs, TarHeader.cs, FileRecord.cs), copyright (c) 2008-2011
 * Kenneth Bell, MIT licensed. Adapted for Yafex: Yafex namespace, no
 * DiscUtils.Streams dependency (exact-read and sub-window implemented
 * locally), entry type renamed. Behavior is unchanged: ustar magic plus
 * checksum validation, space-tolerant octal fields.
 */
#endregion
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Yafex.FileFormats.Tar;

/// <summary>
/// A single tar archive member: name plus its data range relative to
/// the start of the tar stream the owning <see cref="YafexTarReader"/> reads.
/// </summary>
public sealed class YafexTarEntry
{
    internal YafexTarEntry(string name, long offset, long length, bool isDirectory = false)
    {
        Name = name;
        Offset = offset;
        Length = length;
        IsDirectory = isDirectory;
    }

    /// <summary>Member name.</summary>
    public string Name { get; }

    /// <summary>Data offset relative to the tar stream start.</summary>
    public long Offset { get; }

    /// <summary>Data length in bytes.</summary>
    public long Length { get; }

    /// <summary>Directory member (typeflag '5' or a trailing '/').</summary>
    public bool IsDirectory { get; }
}

/// <summary>
/// Minimal read-only ustar reader (vendored DiscUtils logic, no external
/// dependency). Members are validated by magic plus checksum; payloads are
/// exposed as seekable windows over the backing stream, never copied.
/// </summary>
public sealed class YafexTarReader
{
    private const int HeaderLength = 512;

    private readonly List<YafexTarEntry> _ordered = new();
    private readonly Dictionary<string, YafexTarEntry> _files = new();
    private readonly Stream _stream;

    public YafexTarReader(Stream stream)
    {
        _stream = stream;
        byte[] hdrBuf = ReadExact(stream, HeaderLength);
        string name = ReadName(hdrBuf);
        long length = ReadSize(hdrBuf);
        while (length != 0 || !string.IsNullOrEmpty(name))
        {
            if (!IsValidHeader(hdrBuf, 0))
            {
                throw new InvalidDataException("Invalid tar header");
            }

            bool isDir = hdrBuf[156] == (byte)'5' || name.EndsWith('/');
            var record = new YafexTarEntry(name, stream.Position, length, isDir);
            _ordered.Add(record);
            _files[record.Name] = record;
            stream.Position += (length + 511) / 512 * 512;

            hdrBuf = ReadExact(stream, HeaderLength);
            name = ReadName(hdrBuf);
            length = ReadSize(hdrBuf);
        }
    }

    /// <summary>
    /// Probes a buffer for a valid ustar header. Never throws.
    /// </summary>
    public static bool IsHeader(byte[] buffer, int offset)
    {
        return IsValidHeader(buffer, offset);
    }

    /// <summary>All archive members in stored order.</summary>
    public IEnumerable<YafexTarEntry> Entries => _ordered;

    public bool FileExists(string path) => _files.ContainsKey(path);

    /// <summary>Open a member's payload as a seekable window. Never copies.</summary>
    public Stream OpenFile(string path)
    {
        if (_files.TryGetValue(path, out var record))
        {
            return new WindowStream(_stream, record.Offset, record.Length);
        }

        throw new FileNotFoundException("File is not in archive", path);
    }

    private static byte[] ReadExact(Stream stream, int count)
    {
        var buffer = new byte[count];
        int offset = 0;
        while (count > 0)
        {
            int n = stream.Read(buffer, offset, count);
            if (n == 0)
            {
                throw new EndOfStreamException($"Unable to complete read of {buffer.Length} bytes");
            }
            offset += n;
            count -= n;
        }
        return buffer;
    }

    private static string ReadName(byte[] header)
    {
        return Encoding.ASCII.GetString(header, 0, 100).TrimEnd('\0');
    }

    private static long ReadSize(byte[] header)
    {
        return ParseOctal(ReadField(header, 124, 12));
    }

    private static string ReadField(byte[] header, int offset, int length)
    {
        return Encoding.ASCII.GetString(header, offset, length).TrimEnd('\0');
    }

    private static long ParseOctal(string value)
    {
        long result = 0;
        foreach (char c in value)
        {
            // ustar pads numeric fields with leading spaces or zeroes
            if (c == ' ' || c == '\0')
            {
                continue;
            }
            result = result * 8 + (c - '0');
        }
        return result;
    }

    /// <summary>
    /// Valid ustar header: magic plus a matching checksum over a parseable
    /// size field. Never throws.
    /// </summary>
    internal static bool IsValidHeader(byte[] buffer, int offset)
    {
        if (buffer == null || offset < 0 || offset + HeaderLength > buffer.Length)
        {
            return false;
        }

        if (buffer[offset + 257] != (byte)'u'
            || buffer[offset + 258] != (byte)'s'
            || buffer[offset + 259] != (byte)'t'
            || buffer[offset + 260] != (byte)'a'
            || buffer[offset + 261] != (byte)'r')
        {
            return false;
        }

        long sum = 0;
        for (int i = 0; i < HeaderLength; ++i)
        {
            sum += (i >= 148 && i < 156) ? 32 : buffer[offset + i];
        }

        return TryParseOctal(buffer, offset + 148, 8, out long stored)
            && sum == stored
            && TryParseOctal(buffer, offset + 124, 12, out _);
    }

    private static bool TryParseOctal(byte[] buffer, int offset, int length, out long value)
    {
        value = 0;

        int i = 0;
        while (i < length && (buffer[offset + i] == (byte)' ' || buffer[offset + i] == 0))
        {
            ++i;
        }

        if (i >= length)
        {
            return false;
        }

        int digits = 0;
        while (i < length)
        {
            byte b = buffer[offset + i];
            if (b == 0 || b == (byte)' ')
            {
                break;
            }

            if (b < (byte)'0' || b > (byte)'7')
            {
                return false;
            }

            value = (value << 3) + (b - (byte)'0');
            ++digits;
            ++i;
        }

        return digits > 0;
    }

    /// <summary>Read-only seekable window over a shared parent stream.</summary>
    private sealed class WindowStream : Stream
    {
        private readonly Stream _parent;
        private readonly long _base;
        private readonly long _length;
        private long _position;

        public WindowStream(Stream parent, long offset, long length)
        {
            _parent = parent;
            _base = offset;
            _length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _length) return 0;
            count = (int)Math.Min(count, _length - _position);
            _parent.Position = _base + _position;
            int n = _parent.Read(buffer, offset, count);
            _position += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            if (target < 0 || target > _length) throw new IOException("seek out of range");
            _position = target;
            return target;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
