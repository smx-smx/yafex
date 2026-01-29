using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Yafex.Support;

namespace Yafex.FileFormats.MediatekPkg;

[InlineArray(4)]
public struct VendorMagicBytes
{
    private byte _element0;
}

[InlineArray(8)]
public struct MtkMagic {
    private byte _element0;
}

[InlineArray(60)]
public struct VersionBytes
{
    private byte _element0;
}

[InlineArray(32)]
public struct ProductName
{
    private byte _element0;
}

[InlineArray(32)]
public struct Digest
{
    private byte _element0;
}

public struct PkgHeader
{
    public const string MTK_MAGIC = "#DH@FiRm";

    public VendorMagicBytes VendorMagicBytes;
    private MtkMagic _mtkMagic;
    private VersionBytes _version;
    public uint FileSize;
    public uint Flags;
    private ProductName _productName;
    public Digest Digest;

    public string MtkMagic => _mtkMagic.AsString(Encoding.ASCII);
    public string Version => _version.AsString(Encoding.ASCII);
    public string ProductName => _productName.AsString(Encoding.ASCII);
}

[InlineArray(4)]
public struct PartName
{
    private byte _element0;
}

[Flags]
public enum PartFlags : uint
{
    Encrypted = 1 << 0,
    Compressed = 1 << 8
}

public struct PartEntry
{
    private PartName _partName;
    public PartFlags Flags;
    public uint Size;

    public string PartName => _partName.AsString(Encoding.ASCII);
}

[InlineArray(16)]
public struct MtkReserved {
    private byte _element0;
}

[InlineArray(16)]
public struct HmacReserved {
    private byte _element0;
}
[InlineArray(16)]
public struct VendorReserved {
    private byte _element0;
}

public struct DataHeader
{
    public MtkReserved Mtk;
    public HmacReserved Hmac;
    public VendorReserved Vendor;
}

public struct iMtkHeader
{
    [InlineArray(4)]
    public struct iMtkMagic { private byte _element0; }

    private iMtkMagic _magic;

    public uint Length;

    public string Magic => _magic.AsString(Encoding.ASCII);
}