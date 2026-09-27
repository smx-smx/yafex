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

using Yafex.FileFormats.AllwinnerImg;
using Yafex.Support;

namespace Yafex.Fuse;

/// <summary>LiveSuit part payload as a virtual file: RC6-ECB decrypts each
/// 16-byte block independently, so reads only touch their covering blocks
/// (no full-part materialization, RAM stays O(read)).</summary>
public sealed class AwPartFile : IVirtualFile
{
    private readonly IDataSource _input;
    private readonly long _regionStart;

    public AwPartFile(string name, IDataSource input, long regionStart, long storedLength, long originalLength)
    {
        Name = name;
        _input = input;
        _regionStart = regionStart;
        StoredLength = storedLength;
        Length = originalLength;
    }

    public string Name { get; }
    public long StoredLength { get; }
    public long Length { get; }

    public Stream OpenRead() =>
        new Rc6EcbDecryptStream(_input, _regionStart, Length, AwCrypto.PayloadKey());
}

/// <summary>Lazy LiveSuit mount: walks the decrypted entry table (no
/// payload IO at mount) and mounts per-part decrypted views with FAT
/// expansion for filesystem parts (ROOTFS/PRIVFS).</summary>
public static class AllwinnerVfs
{
    public static YafexVfs? Create(MFile input, AllwinnerImgContext ctx)
    {
        try
        {
            var parts = AllwinnerImgReader.ReadTable(input.Data, ctx);
            if (parts.Count == 0) return null;
            var vfs = new YafexVfs();
            var rootDir = new YafexDirectory(input.Name ?? "allwinner", Helpers.OctalLiteral(0755));
            vfs.Root.AddNode(rootDir);
            var keepAlive = new List<object>();
            foreach (var part in parts)
            {
                var name = BaseName(part.FileName);
                if (string.IsNullOrEmpty(name)) name = part.SubType;
                var file = new AwPartFile(name, input, part.Offset, part.StoredLength, part.OriginalLength);
                VfsBuilder.AddImage(rootDir, file,
                    VfsImageViews.Decrypted | VfsImageViews.Expanded | VfsImageViews.Metadata,
                    keepAlive,
                    describe: () => $"type: {part.MainType}\nsubtype: {part.SubType}\n" +
                        $"offset: 0x{part.Offset:X}\nstored: {part.StoredLength}\noriginal: {part.OriginalLength}\nfile: {part.FileName}");
            }
            return vfs;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string BaseName(string path)
    {
        var parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : parts[^1].Trim();
    }
}
