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
using System.Linq;

using Yafex.Fuse;
using Yafex.Support;

namespace Yafex.FileFormats.LxBoot
{
    public record LxSecureBootDetectionResult(int Confidence, LxSecureBootContext? Context) : DetectionResult(Confidence) { }

    public class LxSecureBootAddon : IFormatAddon<LxSecureBootDetectionResult>
    {
        public FileFormat FileFormat => FileFormat.LxSecureBoot;

        public IFormatDetector CreateDetector(IDictionary<string, string> args)
        {
            return new LxSecureBootDetector();
        }

        public IFormatExtractor CreateExtractor(LxSecureBootDetectionResult result)
        {
            return new LxSecureBootExtractor(result.Context);
        }

        public IFormatLister? CreateLister(DetectionResult result)
        {
            if (result is not LxSecureBootDetectionResult typed || typed.Context == null)
            {
                return null;
            }
            return new LxSecureBootLister(typed.Context);
        }

        public IFormatExtractor CreateExtractor<T>(T result) where T : DetectionResult
        {
            throw new System.NotImplementedException();
        }

        public IVfsNode CreateVfsNode(IDataSource ds)
        {
            return new YafexFile(ds, Helpers.OctalLiteral(0444));
        }

        public YafexVfs? CreateVfs(IDataSource source, DetectionResult result)
        {
            if (source is not Support.MFile input) return null;
            if (result is not LxSecureBootDetectionResult typed || typed.Context == null) return null;
            try
            {
                var ctx = typed.Context;
                VfsMount.TryRoot(input, "lxsecureboot", out var vfs, out var root);
                var keepAlive = new System.Collections.Generic.List<object>();
                if (ctx.SpbcVariant == SpbcVariant.Bare)
                {
                    if (!LxSecureBootDetector.TryReadBareSecondStage(input.Data, out uint size))
                    {
                        return null;
                    }
                    VfsMount.AddRange(root, input, "lxboot.bin", 0, size,
                        "bare LL second stage", keepAlive);
                    return vfs;
                }
                var firstOffset = (long)(ctx.SpbcDescriptors.First().LoadAddress & 0xFFFF);
                long firstEnd = ctx.SpbcSecondStageOffset is uint second
                    ? second
                    : input.Data.Length;
                VfsMount.AddRange(root, input, "secureboot_0.bin", firstOffset, firstEnd - firstOffset,
                    $"variant: {ctx.SpbcVariant}", keepAlive);
                if (ctx.SpbcSecondStageOffset is uint secondStage)
                {
                    var header = input.Data.ReadStruct<LxBootHeader>((long)secondStage);
                    VfsMount.AddRange(root, input, "secureboot_1.bin", secondStage, header.Size,
                        $"second stage at 0x{secondStage:X}", keepAlive);
                }
                return vfs;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
