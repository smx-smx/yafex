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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

using Smx.SharpIO;
using Smx.SharpIO.Memory.Buffers;

using Yafex.FileFormats.MediatekPkg;
using Yafex.Metadata;
using Yafex.Support;

using log4net;

namespace Yafex.FileFormats.MediatekPkgOld;

public class MediatekPkgOldExtractor : IFormatExtractor
{
    private static readonly ILog log = LogManager.GetLogger(nameof(MediatekPkgOldExtractor));

    private const string DATA_IV_ID = "mtkpkg-old-data-iv";
    private const string MTK_MAGIC_META = "iMtK";
    private const string MTK_MAGIC_PAD = "iPAd";

    private readonly MediatekPkgOldContext _ctx;
    private readonly KeysRepository _keys;

    public MediatekPkgOldExtractor(MediatekPkgOldContext ctx, KeysRepository keys)
    {
        _ctx = ctx;
        _keys = keys;
    }

    private MtkOldHeader Header => _ctx.Header;

    // Guarded twin of the new-variant metadata strip: best-effort, never throws.
    private static string? ReadOptionalOtaId(Memory<byte> meta)
    {
        if (meta.Length < 4) return null;
        var st = new SpanStream(meta);
        var endOrLength = st.ReadBytes(4);
        if (endOrLength.AsString(Encoding.ASCII) == MTK_MAGIC_PAD) return null;
        var otaIdLength = BinaryPrimitives.ReadInt32LittleEndian(endOrLength);
        if (otaIdLength <= 0 || otaIdLength > st.Length - st.Position) return null;
        return st.ReadBytes(otaIdLength).AsString(Encoding.ASCII);
    }

    private static Memory64<byte> StripMeta(Memory64<byte> data, out string? otaId)
    {
        otaId = null;
        if (data.Length < Unsafe.SizeOf<iMtkHeader>()) return data;
        var st = new SpanStream(data);
        var iMtk = st.ReadStruct<iMtkHeader>();
        if (iMtk.Magic != MTK_MAGIC_META) return data;
        if (iMtk.Length > st.Length - Unsafe.SizeOf<iMtkHeader>()) return data;
        st.Position += Unsafe.SizeOf<iMtkHeader>();
        var metaData = st.ReadBytes((int)iMtk.Length);
        otaId = ReadOptionalOtaId(metaData);
        return data.Slice(st.Position);
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var data = source.Data;
        var st = new SpanStream(data);

        log.Info("Firmware Info");
        log.Info("-------------");
        log.Info($"Vendor magic: {Header.VendorMagic}");
        log.Info($"Version: {Header.Version}");
        log.Info($"Product name: {Header.ProductName}");
        log.Info($"File size: {Header.fileSize}");

        st.Position = _ctx.HeaderOffset + MtkOldHeader.SIZE;

        var basedir = ArtifactBuilder.VersionedDir(source, Header.ProductName);
        source.AddMetadata(new BaseDirectoryPath(basedir));

        var partEntrySize = Unsafe.SizeOf<PartEntry>();
        var iPart = 0;
        while (st.Position < st.Length)
        {
            if (st.Length - st.Position < partEntrySize) break;
            var part = st.ReadStruct<PartEntry>();
            if (part.Size > st.Length - st.Position) break;

            var dataSlice = st.Memory.Slice(st.Position, part.Size);
            st.Position += part.Size;
            iPart++;

            if (part.Flags.HasFlag(PartFlags.Encrypted))
            {
                if (_keys.TryGetRaw(DATA_IV_ID, out var dataIv, out _)
                    && dataIv.Length == 4)
                {
                    var raw = dataSlice.ToArray();
                    dataSlice = new Memory64<byte>(MtkOldCrypto.Decrypt(raw, Header.VendorMagicBytes, dataIv));
                }
                else
                {
                    log.Warn($"Part '{part.PartName}': missing {DATA_IV_ID}, yielding encrypted bytes");
                }
            }

            dataSlice = StripMeta(dataSlice, out var otaId);

            var fileName = $"{part.PartName}.pak";
            var filePath = Path.Combine(basedir, fileName);
            log.Info($"#{iPart}" +
                $"{(part.Flags.HasFlag(PartFlags.Encrypted) ? " [ENCRYPTED]" : "")}" +
                $"{(part.Flags.HasFlag(PartFlags.Compressed) ? " [COMPRESSED]" : "")}" +
                $" saving Part (name='{part.PartName}', size='{part.Size}'" +
                $"{(otaId != null ? $", version='{otaId}'" : "")}" +
                $") to file {filePath}");

            var artifact = new MemoryDataSource(dataSlice) { Name = fileName };
            artifact.SetChildOf(source);
            artifact.AddMetadata(new OutputFileName(fileName));
            artifact.AddMetadata(new OutputDirectoryName(basedir));
            artifact.Flags |= DataSourceFlags.ProcessFurther;
            yield return artifact;
        }
    }
}
