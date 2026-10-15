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
using System.Collections.Generic;

using Yafex.FileFormats.MStarPkg;
using Yafex.Fuse;

namespace Yafex.FileFormats.MstarEcb;

public class MstarEcbAddon : FormatAddon<MstarEcbDetectionResult>
{
    public override FileFormat FileFormat => FileFormat.MstarEcb;

    private readonly KeysRepository _keys;

    public MstarEcbAddon(KeysRepository keys)
    {
        _keys = keys;
    }

    public override IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new MstarEcbDetector(_keys);
    }

    protected override IFormatExtractor CreateExtractor(MstarEcbDetectionResult typed)
    {
        return new MstarEcbExtractor(_keys);
    }

    public override YafexVfs? CreateVfs(IDataSource input, DetectionResult result)
    {
        try
        {
            if (!_keys.TryGetKey(MstarEcb.KeysId, out var entry)) return null;
            var image = input.Data.ToArray();
            if (!MstarEcb.IsValidKey(image, entry.key)) return null;
            var decrypted = new MstarEcbVirtualFile(input.Name ?? "image.ecb", input, entry.key);
            var keepAlive = new List<object> { decrypted };
            // Same member layout as MStarPkg, over the decrypted view.
            var scriptLen = (int)Math.Min(MStarPkgDetector.SCRIPT_PARSE_SIZE, decrypted.Length);
            using var probe = decrypted.OpenRead();
            var head = new byte[scriptLen];
            int got = 0;
            while (got < head.Length)
            {
                int n = probe.Read(head, got, head.Length - got);
                if (n <= 0) break;
                got += n;
            }
            var script = System.Text.Encoding.ASCII.GetString(head, 0, got);
            VfsMount.TryRoot(input, "mstar_ecb", out var vfs, out var root);
            foreach (var part in MStarScript.Parse(script, decrypted.Length))
            {
                var slice = new SliceVirtualFile(
                    $"{part.Name}.pak", decrypted, part.Offset,
                    Math.Min(part.Size, decrypted.Length - part.Offset));
                VfsBuilder.AddImage(root, slice,
                    VfsImageViews.Decrypted | VfsImageViews.Metadata | VfsImageViews.Expanded,
                    keepAlive,
                    describe: () => part.ToString());
            }
            return vfs;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
