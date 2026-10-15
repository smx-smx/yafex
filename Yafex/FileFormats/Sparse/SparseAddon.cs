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
using System.Collections.Generic;

using Yafex.Fuse;

namespace Yafex.FileFormats.Sparse;

public class SparseAddon : FormatAddon<SparseDetectionResult>
{
    public override FileFormat FileFormat => FileFormat.Sparse;

    public override IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new SparseDetector();
    }

    protected override bool IsUsable(SparseDetectionResult result) => result.Layout != null;

    protected override IFormatExtractor CreateExtractor(SparseDetectionResult typed)
    {
        return new SparseExtractor(typed);
    }

    public override YafexVfs? CreateVfs(IDataSource input, DetectionResult result)
    {
        if (result is not SparseDetectionResult typed) return null;
        try
        {
            VfsMount.TryRoot(input, "sparse", out var vfs, out var root);
            var keepAlive = new List<object>();
            var raw = new RangeVirtualFile(input.Name ?? "image", input, 0, input.Data.Length);
            keepAlive.Add(raw);
            var image = new SparseImageVirtualFile(
                (input.Name ?? "image") + ".raw", raw, typed.Layout);
            VfsBuilder.AddImage(root, image,
                VfsImageViews.Decrypted | VfsImageViews.Metadata | VfsImageViews.Expanded,
                keepAlive);
            return vfs;
        }
        catch (System.Exception)
        {
            return null;
        }
    }
}
