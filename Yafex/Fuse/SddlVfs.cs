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
using System.Runtime.CompilerServices;
using System.Text;

using Smx.SharpIO;
using Smx.SharpIO.Memory.Buffers;

using MFile = Yafex.Support.MFile;

using Yafex.FileFormats.SddlSec;
using Yafex.Support;

namespace Yafex.Fuse;

/// <summary>Lazy SDDL module as a virtual file: segment table walks entry
/// headers only (32 bytes decrypted each); payloads decrypt, decipher,
/// decompress and assemble on first access, memoized per module.</summary>
public sealed class SddlModuleFile : IVirtualFile
{
    private readonly MFile _input;
    private readonly SddlSecExtractor _extractor;
    private readonly List<(long DataOffset, uint FileSize)> _segments;
    private readonly object _gate = new();
    private byte[]? _bytes;

    public SddlModuleFile(string name, MFile input, SddlSecExtractor extractor,
        List<(long DataOffset, uint FileSize)> segments)
    {
        Name = name;
        _input = input;
        _extractor = extractor;
        _segments = segments;
    }

    public string Name { get; }

    public long Length
    {
        get
        {
            EnsureAssembled();
            return _bytes!.Length;
        }
    }

    private void EnsureAssembled()
    {
        if (_bytes != null) return;
        lock (_gate)
        {
            if (_bytes != null) return;
            var pieces = new List<(long Dest, byte[] Data)>();
            long end = 0;
            foreach (var (dataOffset, fileSize) in _segments)
            {
                var st = new SpanStream(_input.Data);
                st.Position = dataOffset - EntryHeader.PADDED_SIZE;
                var (_, moduleData) = _extractor.GetFile(st);

                var moduleStream = new SpanStream(moduleData, Endianness.BigEndian);
                var moduleHeader = moduleStream.ReadStruct<ModuleHeader>();
                if (!moduleHeader.HeaderMagic.SequenceEqual(SddlSecHeader.SDDL_SEC_HEADER_MAGIC))
                {
                    throw new InvalidDataException("Invalid module header magic");
                }

                var storedData = moduleStream.ReadBytes(moduleHeader.StoredDataSize);
                var decipheredData = moduleHeader.isCiphered ? SddlSecExtractor.Decipher(storedData) : storedData;
                var finalData = moduleHeader.isCompressed ? SddlSecExtractor.DecompressZlib(decipheredData) : decipheredData;

                var contentReader = new SpanStream(finalData, Endianness.BigEndian);
                var contentHeader = contentReader.ReadStruct<ContentHeader>();
                if (contentHeader.hasSubFile)
                {
                    contentReader.ReadBytes(SddlSecExtractor.SUB_FILE_NAME_LENGHT);
                }
                contentReader.Position = contentHeader.SourceOffset;
                var outData = contentReader.ReadBytes(contentHeader.Size).ToArray();
                pieces.Add((contentHeader.DestOffset, outData));
                end = Math.Max(end, (long)contentHeader.DestOffset + outData.Length);
            }
            var assembled = new byte[end];
            foreach (var (dest, data) in pieces)
            {
                Array.Copy(data, 0, assembled, dest, data.Length);
            }
            _bytes = assembled;
        }
    }

    public Stream OpenRead()
    {
        EnsureAssembled();
        return new MemoryStream(_bytes!, writable: false);
    }
}

/// <summary>Lazy SDDL.SEC mount: SDIT + info texts materialized (small),
/// modules assembled on first access. Header/entry walk only at mount.</summary>
public static class SddlVfs
{
    public static YafexVfs? Create(MFile input, KeysRepository keys, SddlSecDetectionResult result)
    {
        var ctx = result.Context;
        if (ctx == null) return null;
        try
        {
            var extractor = new SddlSecExtractor(ctx, keys);
            var st = new SpanStream(input.Data, Endianness.BigEndian);
            st.Position = Unsafe.SizeOf<SddlSecHeader>();

            var (sditEntry, sditData) = extractor.GetFile(st);
            if (sditEntry.FileName != SddlSecExtractor.SDIT_FILE_NAME) return null;
            var moduleList = extractor.ParseSditToModules(sditData);

            var vfs = new YafexVfs();
            var rootDir = new YafexDirectory(input.Name ?? "sddl", Helpers.OctalLiteral(0755));
            vfs.Root.AddNode(rootDir);
            var keepAlive = new List<object>();

            rootDir.AddNode(new PakVfsFile(
                new MemoryVirtualFile(sditEntry.FileName, sditData.ToArray()), Helpers.OctalLiteral(0444)));

            for (int i = 0; i < ctx.Header.InfoEntriesCount; i++)
            {
                var (infoEntry, infoData) = extractor.GetFile(st);
                rootDir.AddNode(new PakVfsFile(
                    new MemoryVirtualFile(infoEntry.FileName, infoData.ToArray()), Helpers.OctalLiteral(0444)));
            }

            foreach (var module in moduleList)
            {
                var segments = new List<(long DataOffset, uint FileSize)>();
                for (int s = 0; s < module.SegmentCount; s++)
                {
                    var (segEntry, _) = extractor.ReadEntryHeader(st);
                    if (!segEntry.FileName.StartsWith(module.ModuleName)) return null;
                    segments.Add((st.Position, segEntry.FileSize));
                    st.Position += segEntry.FileSize;
                }
                var file = new SddlModuleFile($"{module.ModuleName}.bin", input, extractor, segments);
                VfsBuilder.AddImage(rootDir, file, VfsImageViews.All, keepAlive,
                    describe: () => $"module: {module.ModuleName}\nsegments: {segments.Count}\nversion: {module.VersionString}");
            }
            return vfs;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
