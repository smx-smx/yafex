using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;

using Smx.SharpIO;
using Smx.SharpIO.Extensions;
using Smx.SharpIO.Memory.Buffers;

using Yafex.Metadata;
using Yafex.Support;

using log4net;

namespace Yafex.FileFormats.MediatekPkg;

public class MediatekPkgExtractor : IFormatExtractor
{
    private readonly MediatekPkgContext _ctx;
    private readonly KeysRepository _keys;

    private static readonly ILog log = LogManager.GetLogger(nameof(MediatekPkgExtractor));
    private AesDecryptor? _decryptor;
    private bool _keySearchAttempted = false;

    private const string MTK_RESERVED_MAGIC = "reserved mtk inc";

    public MediatekPkgExtractor(
        MediatekPkgContext ctx,
        KeysRepository keys
    )
    {
        _ctx = ctx;
        _keys = keys;
        _decryptor = null;
    }

    private bool IsDecryptedHeader(ReadOnlySpan64<byte> data)
    {
        return SpanEx.Cast<DataHeader>(data)[0]
            .Mtk.AsString(Encoding.ASCII) == MTK_RESERVED_MAGIC;
    }

    private PkgHeader Header => _ctx.Header.Span[0];

    private Memory64<byte> DecryptUnalign(Memory64<byte> data, AesDecryptor decryptor)
    {
        //if all data is aligned to 16 bytes(no tail), just return the AES decrypted data.
        if (data.Length % 16 == 0) {
            return decryptor.Decrypt(data.Span);
        }

        var alignedLen = data.Length - (data.Length % 16);
        var alignedSlice = data.Slice(0, alignedLen);
        var unalignedSlice = data.Slice(alignedLen);

        //AES decrypt data aligned to 16 bytes
        var decrypted = decryptor.Decrypt(alignedSlice.Span, data.Length);

        //deXOR unaligned data with the AES key.
        var aesKey = decryptor.Aes.Key;
        var outTailSpan = decrypted.Span.Slice(alignedLen);
        for (int i = 0; i < unalignedSlice.Length; i++)
        {
            outTailSpan[i] = (byte)(unalignedSlice.Span[i] ^ aesKey[i % aesKey.Length]);
        }

        return decrypted;
    }

    private Memory64<byte> AttemptDecrypt(Memory64<byte> data)
    {
        if(_decryptor != null)
        {
            return DecryptUnalign(data, _decryptor);
        }

        if (_keySearchAttempted)
        {
            // we couldn't find a key, return data as-is
            return data;
        }

        _keySearchAttempted = true;

        var decryptor = _keys.CreateAesDecryptor("mtkpkg-keys", data.Span, IsDecryptedHeader);
        if(decryptor == null) 
        {
            // we couldn't find a key
            // Try to decrypt by using vendorMagic repeated 4 times, ivec 0
            byte[] vendorKeyBytes = [
                ..Header.VendorMagicBytes,
                ..Header.VendorMagicBytes,
                ..Header.VendorMagicBytes,
                ..Header.VendorMagicBytes
            ];

            var vendorKey = new KeyEntry
            {
                keyAlgo = CipherAlgorithmType.Aes128,
                keyMode = CipherMode.CBC,
                key = vendorKeyBytes,
                iv = new byte[16],
                comment = Encoding.ASCII.GetString(vendorKeyBytes)
            }.GetAes();

            decryptor = new AesDecryptor(vendorKey);
            data = DecryptUnalign(data, decryptor);
            if (IsDecryptedHeader(data.Span)) 
            {
                _decryptor = decryptor;
            }
            return data;
        }

        _decryptor = decryptor;
        return DecryptUnalign(data, _decryptor);
    }

    private const string MTK_MAGIC_META = "iMtK";
    private const string MTK_MAGIC_PAD = "iPAd";

    private string? ReadOptionalOtaId(Memory<byte> meta)
    {
        var st = new SpanStream(meta);
        var endOrLength = st.ReadBytes(4);
        if(endOrLength.AsString(Encoding.ASCII) == MTK_MAGIC_PAD)
        {
            return null;
        }
        var otaIdLength = BinaryPrimitives.ReadInt32LittleEndian(endOrLength);
        var otaId = st.ReadBytes(otaIdLength).AsString(Encoding.ASCII);
        return otaId;
    }

    private Memory64<byte> ReadMtkMetadata(Memory64<byte> data, out string? otaId)
    {
        var st = new SpanStream(data);
        var iMtk = st.PerformAt(st.Position, st.ReadStruct<iMtkHeader>);

        otaId = null;

        if (iMtk.Magic == MTK_MAGIC_META)
        {
            st.Position += Unsafe.SizeOf<iMtkHeader>();

            var metaData = st.ReadBytes(iMtk.Length);
            otaId = ReadOptionalOtaId(metaData);
        }

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
        log.Info($"File size: {Header.FileSize}");

        st.Position = Unsafe.SizeOf<PkgHeader>();
        if (_ctx.Variant == MtkPkgVariant.Standard)
        {
            st.Position += PkgHeader.STANDARD_DIGEST_SIZE;
        }
        if (_ctx.Variant == MtkPkgVariant.New)
        {
            st.Position += PkgHeader.NEW_DIGEST_SIZE;
        }
        if (_ctx.Flags.HasFlag(MtkPkgQuirks.Philips))
        {
            st.Position += MediatekPkgDetector.PHILIPS_EXTRA_HEADER_SIZE;
        }
        
        var dataHeaderSize = _ctx.Variant == MtkPkgVariant.Standard
            ? Unsafe.SizeOf<DataHeader>()
            : 0;

        var basedir = Path.Combine(source.RequireBaseDirectory(), Header.ProductName);
        source.AddMetadata(new BaseDirectoryPath(basedir));

        var iPart = 0;
        while (st.Position < st.Length)
        {
            if (_ctx.Flags.HasFlag(MtkPkgQuirks.Philips) && (st.Position == st.Length - MediatekPkgDetector.PHILIPS_FOOTER_SIGNATURE_SIZE))
            {
                break;
            }

            var part = st.ReadStruct<PartEntry>();
            
            var dataOffset = st.Position;
            var dataSize = part.Size + Unsafe.SizeOf<DataHeader>();
            var dataSlice = st.Memory.Slice(dataOffset, dataSize);

            if (part.Flags.HasFlag(PartFlags.Encrypted))
            {
                // replace the crypted memory view with its decrypted form
                dataSlice = AttemptDecrypt(dataSlice);
            }

            // skip data header
            dataSlice = dataSlice.Slice(Unsafe.SizeOf<DataHeader>());
            dataSlice = ReadMtkMetadata(dataSlice, out var otaId);

            st.Position += dataSize;
            iPart++;

            var fileName = $"{part.PartName}.pak";
			var filePath = Path.Combine(basedir, fileName);

            log.Info($"#{iPart}" +
                    $"{(part.Flags.HasFlag(PartFlags.Encrypted) ? " [ENCRYPTED]" : "")}" + 
                    $"{(part.Flags.HasFlag(PartFlags.Compressed) ? " [COMPRESSED]" : "")}" + 
                    $" saving Part (name='{part.PartName}'," +
					$" offset=0x{dataOffset:X}," +
					$" size='{part.Size}'" +
                    $"{(otaId != null ? $", version='{otaId}'" : "")}" +
                    $") to file {filePath}");

            var artifact = new MemoryDataSource(dataSlice)
            {
                Name = fileName
            };
            artifact.SetChildOf(source);
            artifact.AddMetadata(new OutputFileName(fileName));
            artifact.AddMetadata(new OutputDirectoryName(basedir));
            artifact.Flags |= DataSourceFlags.ProcessFurther;
            yield return artifact;
        }
    }
}