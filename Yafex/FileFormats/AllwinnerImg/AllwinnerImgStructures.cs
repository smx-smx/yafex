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
using System.Runtime.CompilerServices;
using System.Text;

using Yafex.Support;

// Binary-layout structs populated via ReadStruct/Marshal: padding and
// reserved fields are never assigned/read in C# by design (CS0649/CS0169).
#pragma warning disable CS0649, CS0169

namespace Yafex.FileFormats.AllwinnerImg;

[InlineArray(8)]
public struct AwMagic
{
    private byte _element0;
}

[InlineArray(8)]
public struct AwMainType
{
    private byte _element0;
}

[InlineArray(16)]
public struct AwSubType
{
    private byte _element0;
}

[InlineArray(256)]
public struct AwFileName
{
    private byte _element0;
}

/// <summary>Classic LiveSuit image header (first 1024 bytes RC6-decrypted
/// with the header key). v1 (0x100) and v3 (0x300) differ by one leading
/// word in the id block; the fixed prefix is identical.</summary>
public struct AwHeader
{
    public const string Magic = "IMAGEWTY";
    public const uint Version1 = 0x100;
    public const uint Version3 = 0x300;
    public const int EncryptedSize = 1024;
    public const int EntrySize = 1024;

    private AwMagic _magic;
    public uint HeaderVersion;
    public uint HeaderSize;
    public uint RamBase;
    public uint FormatVersion;
    public uint ImageSize;
    public uint ImageHeaderSize;

    public string MagicString => _magic.AsString(Encoding.ASCII);
}

public struct AwIdBlockV1
{
    public uint Pid;
    public uint Vid;
    public uint HardwareId;
    public uint FirmwareId;
    public uint Val1;
    public uint Val1024;
    public uint NumFiles;
    public uint Val1024_2;
    public uint Val0;
    public uint Val0_2;
    public uint Val0_3;
    public uint Val0_4;
}

/// <summary>One 1024-byte file entry (RC6-decrypted with the entries key).
/// String tables are space/null padded.</summary>
public struct AwEntry
{
    public uint FileNameLen;
    public uint TotalHeaderSize;
    private AwMainType _mainType;
    private AwSubType _subType;

    public string MainType => _mainType.AsString(Encoding.ASCII).TrimEnd(' ', '\0');
    public string SubType => _subType.AsString(Encoding.ASCII).TrimEnd(' ', '\0');
}

public struct AwEntryV1
{
    public uint Unknown3;
    public uint StoredLength;
    public uint OriginalLength;
    public uint Offset;
    public uint Unknown;
    private AwFileName _fileName;

    public string FileName => _fileName.AsString(Encoding.ASCII).TrimEnd(' ', '\0');
}

public struct AwEntryV3
{
    public uint Unknown0;
    private AwFileName _fileName;
    public uint StoredLength;
    public uint Pad1;
    public uint OriginalLength;
    public uint Pad2;
    public uint Offset;

    public string FileName => _fileName.AsString(Encoding.ASCII).TrimEnd(' ', '\0');
}
