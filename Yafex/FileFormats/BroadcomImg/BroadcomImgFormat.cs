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
using Yafex.Support;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Yafex.FileFormats.Jffs2;
using Yafex.FileFormats.Ubi;
using Yafex.FileSystems.Jffs2;
using Yafex.FileSystems.Ubi;

namespace Yafex.FileFormats.BroadcomImg;

/// <summary>
/// Raw NAND flash layout pairing a JFFS2 boot region with a UBI region
/// (e.g. Broadcom/Askey whole-flash dumps: CFE + kernel in JFFS2, rootfs
/// in UBI/UBIFS). Detected by a fully validated JFFS2 image at offset zero
/// followed by CRC-validated UBI eraseblocks; both regions are listed,
/// extracted, and mount-expanded through their own formats.
/// </summary>
public static class BroadcomImgParser
{
    public const long MinUbiStart = 65536;

    public static bool TrySplit(byte[] image, out long ubiStart)
    {
        ubiStart = -1;
        if (image.Length < MinUbiStart + 4096) return false;
        // JFFS2 must fully validate at offset zero (not just the magic)
        try
        {
            using var head = new MemoryStream(image, writable: false);
            using var fs = new Jffs2FileSystem(head);
            if (fs.Root == null) return false;
        }
        catch (Exception)
        {
            return false;
        }
        long start = UbiFormat.FindStart(image);
        if (start < MinUbiStart) return false;
        if (UbiFormat.OpenAt(image, start) == null) return false;
        ubiStart = start;
        return true;
    }
}

public record BroadcomImgDetectionResult(int Confidence, byte[]? Image, long UbiStart) : DetectionResult(Confidence);

public class BroadcomImgDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < BroadcomImgParser.MinUbiStart + 4096)
            {
                return DetectionResult.Fail;
            }
            // sliced pre-checks: JFFS2 magic at zero, a CRC-valid UBI header
            using (var head = new MemoryStream(DataScan.Window(data, 0, 65536), writable: false))
            {
                if (!Jffs2FileSystem.Detect(head) || !UbiFormat.HasValidEc(data))
                {
                    return DetectionResult.Fail;
                }
            }
            var bytes = data.ToArray();
            if (!BroadcomImgParser.TrySplit(bytes, out long ubiStart))
            {
                return DetectionResult.Fail;
            }
            return new BroadcomImgDetectionResult(95, bytes, ubiStart);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class BroadcomImgExtractor : IFormatExtractor
{
    private readonly byte[] _image;
    private readonly long _ubiStart;

    public BroadcomImgExtractor(BroadcomImgDetectionResult result)
    {
        _image = result.Image!;
        _ubiStart = result.UbiStart;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        // boot region through the JFFS2 extractor on a sliced view.
        // The slice parents to the real source so member artifacts
        // resolve output paths (otherwise they would be silently dropped).
        var jffs = new byte[_ubiStart];
        Array.Copy(_image, jffs, _ubiStart);
        var mem = new MemoryDataSource(jffs) { Name = source.Name, Flags = DataSourceFlags.Input };
        mem.SetChildOf(source);
        var jdet = new Jffs2Detector().Detect(mem);
        if (jdet is Jffs2DetectionResult jtyped && jtyped.Image != null)
        {
            foreach (var a in new Jffs2Extractor(jtyped).Extract(mem))
            {
                yield return a;
            }
        }
        // UBI volumes through the UBI extractor on the trailing view
        var ubiBytes = new byte[_image.Length - _ubiStart];
        Array.Copy(_image, _ubiStart, ubiBytes, 0, ubiBytes.Length);
        var umem = new MemoryDataSource(ubiBytes) { Name = source.Name, Flags = DataSourceFlags.Input };
        umem.SetChildOf(source);
        var udet = new UbiDetector().Detect(umem);
        if (udet is UbiDetectionResult utyped && utyped.Image != null)
        {
            foreach (var a in new UbiExtractor(utyped).Extract(umem))
            {
                yield return a;
            }
        }
    }
}
