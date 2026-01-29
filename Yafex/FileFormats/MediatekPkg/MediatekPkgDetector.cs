using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

using Smx.SharpIO;
using Smx.SharpIO.Extensions;

using Yafex.Support;

namespace Yafex.FileFormats.MediatekPkg;

public class MediatekPkgDetector : IFormatDetector
{
    private const string HEADER_KEY_ID = "mtkpkg-header-key";
    private readonly Aes _headerKey;

    public const int PHILIPS_DIGEST_SIZE = 0x80;

    private readonly MediatekPkgContext _ctx;
    private AesDecryptor _decryptor;


    public MediatekPkgDetector(KeysRepository keys)
    {
        var headerKey = keys.GetKey(HEADER_KEY_ID);
        _headerKey = headerKey.GetAes();
        _decryptor = new AesDecryptor(_headerKey);
        _ctx = new MediatekPkgContext();
    }

    private static bool IsDecryptedHeader(Memory<byte> bytes)
    {
        var hdr = bytes.Cast<PkgHeader>()[0];
        return hdr.MtkMagic == PkgHeader.MTK_MAGIC;
    }

    private bool TryDecryptHeader(Memory<byte> bytes, [MaybeNullWhen(false)] out Memory<PkgHeader> header)
    {
        var decrypted = _decryptor.Decrypt(bytes.Span);
        if(IsDecryptedHeader(decrypted))
        {
            header = decrypted.Cast<byte, PkgHeader>();
            return true;
        } else
        {
            header = null;
            return false;
        }
    }

    public DetectionResult Detect(IDataSource source)
    {
        var data = source.Data;
        
        var headerSize = Unsafe.SizeOf<PkgHeader>();
        var headerBytes = data.Slice(0, headerSize);

        Memory<PkgHeader> hdr;
        if(TryDecryptHeader(headerBytes, out hdr))
        {
            _ctx.Header = hdr;
            _ctx.Variant = MtkPkgVariant.Standard;
            return new DetectionResult(100, _ctx);
        }

        /* It failed, but we want to check for Philips.
		 * Philips has an additional 0x80 header before the normal PKG one
		 */
        headerBytes = data.Slice(PHILIPS_DIGEST_SIZE, headerSize);
        if(TryDecryptHeader(headerBytes, out hdr))
        {
            _ctx.Header = hdr;
            _ctx.Variant = MtkPkgVariant.Standard;
            _ctx.Flags |= MtkPkgQuirks.Philips;
            return new DetectionResult(100, _ctx);
        }

        return new DetectionResult(0, null);
    }
}