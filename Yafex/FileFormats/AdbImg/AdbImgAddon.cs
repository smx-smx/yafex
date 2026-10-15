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

using Yafex.Fuse;
using Yafex.Support;

namespace Yafex.FileFormats.AdbImg;

public class AdbImgAddon : FormatAddon<AdbImgDetectionResult>
{
    public override FileFormat FileFormat => FileFormat.AdbImg;

    public override IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new AdbImgDetector();
    }

    protected override bool IsUsable(AdbImgDetectionResult result) => result.Image != null;

    protected override IFormatExtractor CreateExtractor(AdbImgDetectionResult typed)
    {
        return new AdbImgExtractor(typed);
    }

    protected override IFormatLister? CreateLister(AdbImgDetectionResult typed)
    {
        return new AdbImgLister(typed);
    }

    // Lazy mount: a range view of the payload plus the signature blob,
    // mirroring the extractor.
    public override YafexVfs? CreateVfs(IDataSource source, DetectionResult result)
    {
        if (source is not MFile input)
        {
            return null;
        }
        if (result is not AdbImgDetectionResult typed || typed.Image == null)
        {
            return null;
        }
        try
        {
            VfsMount.TryRoot(input, "adbimg", out var vfs, out var root);
            var keepAlive = new List<object>();
            VfsMount.AddRange(root, input, "payload.jffs2", typed.PayloadStart,
                typed.PayloadEnd - typed.PayloadStart, "mIMG payload", keepAlive);
            if (typed.PayloadEnd < typed.Image.Length)
            {
                VfsMount.AddRange(root, input, "signature.bin", typed.PayloadEnd,
                    typed.Image.Length - typed.PayloadEnd, "signature tail", keepAlive);
            }
            return vfs;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
