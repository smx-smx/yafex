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

using Yafex.Metadata;
using Yafex.Support;

namespace Yafex.FileFormats.MediatekPkg;

public class MediatekPkgExtractor : IFormatExtractor
{
    private readonly MediatekPkgContext _ctx;
    private readonly KeysRepository _keys;

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

    private bool IsDecryptedHeader(ReadOnlySpan<byte> data)
    {
        return SpanEx.Cast<DataHeader>(data)[0]
            .Mtk.AsString(Encoding.ASCII) == MTK_RESERVED_MAGIC;
    }

    private PkgHeader Header => _ctx.Header.Span[0];

    private Memory<byte> AttemptDecrypt(Memory<byte> data)
    {
        if(_decryptor != null)
        {
            return _decryptor.Decrypt(data.Span);
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
            data = decryptor.Decrypt(data.Span);
            if (IsDecryptedHeader(data.Span)) 
            {
                _decryptor = decryptor;
            }
            return data;
        }

        _decryptor = decryptor;
        return decryptor.Decrypt(data.Span);
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

    private Memory<byte> ReadMtkMetadata(Memory<byte> data, out string? otaId)
    {
        var st = new SpanStream(data);
        var iMtk = st.PerformAt(st.Position, st.ReadStruct<iMtkHeader>);

        otaId = null;

        if (iMtk.Magic == MTK_MAGIC_META)
        {
            st.Position += Unsafe.SizeOf<iMtkHeader>();

            var metaData = st.ReadBytes((int)iMtk.Length);
            otaId = ReadOptionalOtaId(metaData);
        }

        return data.Slice((int)st.Position);
    }


    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var data = source.Data;
        
        var st = new SpanStream(data);
        if (_ctx.Flags.HasFlag(MtkPkgQuirks.Philips))
        {
            st.Position += MediatekPkgDetector.PHILIPS_DIGEST_SIZE;
        }
        st.Position = Unsafe.SizeOf<PkgHeader>();

        var dataHeaderSize = _ctx.Variant == MtkPkgVariant.Standard
            ? Unsafe.SizeOf<DataHeader>()
            : 0;

        var basedir = Path.Combine(source.RequireBaseDirectory(), Header.ProductName);
        source.AddMetadata(new BaseDirectoryPath(basedir));

        while (st.Position < st.Length)
        {
            var part = st.ReadStruct<PartEntry>();
            
            var dataOffset = st.Position;
            var dataSize = (int)part.Size + Unsafe.SizeOf<DataHeader>();
            var dataSlice = st.Memory.Slice((int)dataOffset, dataSize);

            if (part.Flags.HasFlag(PartFlags.Encrypted))
            {
                // replace the crypted memory view with its decrypted form
                dataSlice = AttemptDecrypt(dataSlice);
            }

            // skip data header
            dataSlice = dataSlice.Slice(Unsafe.SizeOf<DataHeader>());
            dataSlice = ReadMtkMetadata(dataSlice, out var otaId);

            if(otaId != null)
            {
                // $TODO: print
            }

            st.Position += dataSize;

            var artifact = new MemoryDataSource(dataSlice);
            artifact.SetChildOf(source);
            artifact.AddMetadata(new OutputFileName($"{part.PartName}.pak"));
            artifact.AddMetadata(new OutputDirectoryName(basedir));
            artifact.Flags |= DataSourceFlags.ProcessFurther;
            yield return artifact;
        }
    }
}