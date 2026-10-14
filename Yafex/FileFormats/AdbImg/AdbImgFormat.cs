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
using System.Linq;
using System.Text;

using Yafex.FileFormats.Jffs2;
using Yafex.FileSystems.Jffs2;

namespace Yafex.FileFormats.AdbImg;

/// <summary>
/// ADB Epicentro firmware container (e.g. DVA-5592 .sig files): a 256-byte
/// outer header ("yIMG1", board id, build date, ASCII data length) wrapping
/// a 256-byte single-board header ("mIMG1") and the payload (a JFFS2 boot
/// filesystem on observed images, with YAPS partition splits), plus a
/// signature tail past the stated data length. The payload is exposed per
/// its own format; the tail is kept as one opaque signature blob.
/// </summary>
public static class AdbImgParser
{
    public const int OuterHeaderSize = 256;
    public const int InnerHeaderSize = 256;
    public const int DataLengthOff = 0x58;
    public const int DataLengthMax = 16;

    public static string ReadAsciiZ(byte[] image, long offset, int max)
    {
        int len = 0;
        while (len < max && offset + len < image.Length && image[offset + len] != 0) len++;
        return Encoding.ASCII.GetString(image, (int)offset, len);
    }

    /// <summary>ASCII decimal field, tolerant of NUL/space padding.</summary>
    public static bool TryReadDecimal(byte[] image, long offset, int max, out long value)
    {
        value = 0;
        long at = offset;
        long end = Math.Min(offset + max, image.Length);
        while (at < end && (image[at] == 0 || image[at] == (byte)' ')) at++;
        int digits = 0;
        while (at < end && image[at] >= (byte)'0' && image[at] <= (byte)'9')
        {
            value = value * 10 + (image[at] - (byte)'0');
            if (value > image.Length) return false;
            at++;
            digits++;
        }
        return digits > 0 && digits <= 12;
    }

    public static bool TrySplit(byte[] image, out long payloadStart, out long payloadEnd)
    {
        payloadStart = 0;
        payloadEnd = 0;
        if (image.Length < OuterHeaderSize + InnerHeaderSize + 64) return false;
        if (ReadAsciiZ(image, 0, 6) != "yIMG1") return false;
        if (ReadAsciiZ(image, OuterHeaderSize, 6) != "mIMG1") return false;
        if (!TryReadDecimal(image, DataLengthOff, DataLengthMax, out long dataLength)) return false;
        if (dataLength <= InnerHeaderSize || OuterHeaderSize + dataLength > image.Length) return false;
        payloadStart = OuterHeaderSize + InnerHeaderSize;
        payloadEnd = OuterHeaderSize + dataLength;
        return true;
    }

    public static string SafeName(string name)
    {
        string flat = name.Replace('/', '_').Replace('\\', '_').Trim();
        return flat.Length == 0 ? "part" : flat;
    }
}

public record AdbImgDetectionResult(int Confidence, byte[]? Image, long PayloadStart, long PayloadEnd) : DetectionResult(Confidence);

public class AdbImgDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < AdbImgParser.OuterHeaderSize + AdbImgParser.InnerHeaderSize + 64)
            {
                return DetectionResult.Fail;
            }
            var bytes = data.ToArray();
            if (!AdbImgParser.TrySplit(bytes, out long start, out long end))
            {
                return DetectionResult.Fail;
            }
            return new AdbImgDetectionResult(90, bytes, start, end);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class AdbImgLister : IFormatLister
{
    private readonly byte[] _image;
    private readonly long _start;
    private readonly long _end;

    public AdbImgLister(AdbImgDetectionResult result)
    {
        _image = result.Image!;
        _start = result.PayloadStart;
        _end = result.PayloadEnd;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        string board = AdbImgParser.ReadAsciiZ(_image, 0x30, 16);
        string date = AdbImgParser.ReadAsciiZ(_image, 0x40, 16);
        output.WriteLine($"AdbImg ({source.Name}, board {board}, built {date}, payload 0x{_start:X}..0x{_end:X}):");
        var payload = new byte[_end - _start];
        Array.Copy(_image, _start, payload, 0, payload.Length);
        using var ms = new MemoryStream(payload, writable: false);
        var ctx = Jffs2Context.Open(ms);
        Walk(ctx, 1, "", output);
        if (_end < _image.Length)
        {
            output.WriteLine($"  s signature.bin size={_image.Length - _end}");
        }
    }

    private static void Walk(Jffs2Context ctx, uint ino, string prefix, TextWriter output)
    {
        foreach (var e in ctx.ReadDirectory(ino).OrderBy(x => x.Name))
        {
            var inode = ctx.GetInode(e.Ino);
            bool dir = e.Dtype == 4 || (inode.Mode & 0xF000) == 0x4000;
            bool link = !dir && (e.Dtype == 10 || (inode.Mode & 0xF000) == 0xA000);
            var kind = dir ? "d" : link ? "l" : "f";
            output.WriteLine($"  {kind} {prefix}{e.Name} size={inode.Size}");
            if (dir)
            {
                Walk(ctx, e.Ino, prefix + e.Name + "/", output);
            }
        }
    }
}

public class AdbImgExtractor : IFormatExtractor
{
    private readonly byte[] _image;
    private readonly long _start;
    private readonly long _end;

    public AdbImgExtractor(AdbImgDetectionResult result)
    {
        _image = result.Image!;
        _start = result.PayloadStart;
        _end = result.PayloadEnd;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var payload = new byte[_end - _start];
        Array.Copy(_image, _start, payload, 0, payload.Length);
        var mem = new MemoryDataSource(payload) { Name = source.Name, Flags = DataSourceFlags.Input };
        mem.SetChildOf(source);
        var jdet = new Jffs2Detector().Detect(mem);
        if (jdet is Jffs2DetectionResult jtyped && jtyped.Image != null)
        {
            foreach (var a in new Jffs2Extractor(jtyped).Extract(mem))
            {
                yield return a;
            }
        }
        if (_end < _image.Length)
        {
            var sig = new byte[_image.Length - _end];
            Array.Copy(_image, _end, sig, 0, sig.Length);
            yield return ArtifactBuilder.Flat(source, "signature.bin", sig);
        }
    }
}
