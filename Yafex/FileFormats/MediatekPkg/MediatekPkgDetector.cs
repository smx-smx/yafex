using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

using Smx.SharpIO;
using Smx.SharpIO.Extensions;
using Smx.SharpIO.Memory.Buffers;

using Yafex.Support;

namespace Yafex.FileFormats.MediatekPkg;

public record MediatekPkgDetectionResult(int Confidence, MediatekPkgContext? Context) : DetectionResult(Confidence);

public class MediatekAesDecryptor : AesDecryptor
{
    public MediatekAesDecryptor(Aes aes) : base(aes)
    {
    }

    protected override ReadOnlySpan<byte> HandleUnalignedData(ReadOnlySpan<byte> data)
    {
        var result = new Span<byte>(new byte[data.Length]);
        data.CopyTo(result);

        //deXOR unaligned data with the AES key.
        var aesKey = Aes.Key;
        for (int i = 0; i < data.Length; i++)
        {
            result[i] ^= aesKey[i % aesKey.Length];
        }

        return result;
    }
}

public class MediatekAesDecryptorFactory : IAesDecryptorFactory<MediatekAesDecryptor>
{
    public MediatekAesDecryptor Create(Aes aes)
    {
        return new MediatekAesDecryptor(aes);
    }
}

public class MediatekPkgDetector : IFormatDetector
{
    private const string HEADER_KEY_ID = "mtkpkg-header-key";
    private readonly KeysRepository _keys;

    public const int PHILIPS_EXTRA_HEADER_SIZE = 0x80;
    public const int PHILIPS_FOOTER_SIGNATURE_SIZE = 0x100;

    private readonly MediatekPkgContext _ctx;
    private readonly AesDecryptor? _standardDecryptor;

    public MediatekPkgDetector(KeysRepository keys)
    {
        if (keys.TryGetKey(HEADER_KEY_ID, out var standardHeaderKey))
        {
            _standardDecryptor = new MediatekAesDecryptor(standardHeaderKey.GetAes());
        }
        _keys = keys;
        _ctx = new MediatekPkgContext();
    }

    private static bool IsDecryptedHeader(ReadOnlySpan64<byte> bytes)
    {
        var hdr = SpanEx.Cast<PkgHeader>(bytes)[0];
        return hdr.MtkMagic == PkgHeader.MTK_MAGIC;
    }

    private bool TryDecryptHeaderStandard(Memory64<byte> bytes, out Memory64<PkgHeader> header)
    {
        header = default;
        if (_standardDecryptor == null) return false;
        var decrypted = _standardDecryptor.Decrypt(bytes.Span);
        if(IsDecryptedHeader(decrypted.Span))
        {
            header = decrypted.Cast<byte, PkgHeader>();
            return true;
        } else
        {
            header = default;
            return false;
        }
    }

    private bool TryDecryptHeaderNew(Memory64<byte> bytes, out Memory64<PkgHeader> header)
    {
        var decryptor = _keys.CreateAesDecryptor("mtkpkg-keys", new MediatekAesDecryptorFactory(), bytes.Span, IsDecryptedHeader);
        if(decryptor != null)
        {
            var decrypted = decryptor.Decrypt(bytes.Span);
            header = decrypted.Cast<byte, PkgHeader>();
            return true;
        } else
        {
            header = default;
            return false;
        }
    }

    public DetectionResult Detect(IDataSource source)
    {
        var data = source.Data;
        
        var headerSize = Unsafe.SizeOf<PkgHeader>();
        var headerBytes = data.Slice(0, headerSize);

        Memory64<PkgHeader> hdr;
        if(TryDecryptHeaderStandard(headerBytes, out hdr))
        {
            _ctx.Header = hdr;
            _ctx.Variant = MtkPkgVariant.Standard;
            return new MediatekPkgDetectionResult(100, _ctx);
        }

        if(TryDecryptHeaderNew(headerBytes, out hdr))
        {
            _ctx.Header = hdr;
            _ctx.Variant = MtkPkgVariant.New;
            return new MediatekPkgDetectionResult(100, _ctx);
        }

        /* It failed, but we want to check for Philips.
		 * Philips has an additional 0x80 header before the normal PKG one
		 */
        headerBytes = data.Slice(PHILIPS_EXTRA_HEADER_SIZE, headerSize);
        if(TryDecryptHeaderStandard(headerBytes, out hdr))
        {
            _ctx.Header = hdr;
            _ctx.Variant = MtkPkgVariant.Standard;
            _ctx.Flags |= MtkPkgQuirks.Philips;
            return new MediatekPkgDetectionResult(100, _ctx);
        }

        return new MediatekPkgDetectionResult(0, null);
    }
}