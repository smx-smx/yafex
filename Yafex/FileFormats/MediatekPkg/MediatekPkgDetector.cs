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

public class MediatekPkgDetector : IFormatDetector
{
    private const string HEADER_KEY_ID = "mtkpkg-header-key";
    private readonly Aes _standardHeaderKey;
    private readonly KeysRepository _keys;

    public const int PHILIPS_EXTRA_HEADER_SIZE = 0x80;
    public const int PHILIPS_FOOTER_SIGNATURE_SIZE = 0x100;

    private readonly MediatekPkgContext _ctx;
    private readonly AesDecryptor _standardDecryptor;

    public MediatekPkgDetector(KeysRepository keys)
    {
        var standardHeaderKey = keys.GetKey(HEADER_KEY_ID);
        _standardHeaderKey = standardHeaderKey.GetAes();
        _standardDecryptor = new AesDecryptor(_standardHeaderKey);
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
        var decryptor = _keys.CreateAesDecryptor("mtkpkg-keys", bytes.Span, IsDecryptedHeader);
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