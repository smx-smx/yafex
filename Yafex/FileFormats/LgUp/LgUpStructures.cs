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
using System.Text;

namespace Yafex.FileFormats.LgUp;

/// <summary>
/// Constants for the LG "ELIX" LGUP package format (.up/.epk files with
/// EPKG/LGUA wrappers and LGUP v4/v5 encrypted headers).
/// Ported from the elix lgup tool (CInstallFactory install types,
/// CInstallext4 unit types, ECompressType).
/// </summary>
public static class LgUpFormat
{
    public const int SignatureSize = 0x100;

    public const string OuterMagicEpkg = "EPKG";
    public const string OuterMagicLgua = "LGUA";
    public const string InnerMagic = "LGUP";

    public static readonly byte[] Iv = Encoding.ASCII.GetBytes("DlfEmdEkfTjdGkWk");

    // Install (section) types - CInstallFactory::_CreateInstall50
    public const uint InstallRaw = 0xF0102001;
    public const uint InstallFull = 0xF0102002;
    public const uint InstallAbi = 0xF0102003;
    public const uint InstallFs = 0xF0102004;
    public const uint InstallExt4 = 0xF0102005;
    public const uint InstallBootp = 0xF0102006;
    public const uint InstallDummy = 0xF0102007;
    public const uint InstallBootpDummy = 0xF0102008;
    public const uint InstallGpt = 0xF010200A;
    public const uint InstallPartitionControl = 0xF010200B;
    public const uint InstallSuperMetadata = 0xF010200C;
    public const uint InstallSuperMetadataDummy = 0xF010200D;
    public const uint InstallUbi = 0xF010200E;
    public const uint InstallSubFirmware = 0xF010200F;
    public const uint InstallYaffs2 = 0xF0102010;

    // Unit types - CInstallext4::Update
    public const uint UnitPatch = 0xF9F9F00;   // bsdiff40/imgdiff2 vs source
    public const uint UnitMove = 0xF9F9F01;    // copy source -> target
    public const uint UnitNew = 0xF9F9F02;     // write (decompressed) payload to target
    public const uint UnitSetData = 0xF9F9F03; // fill target with byte value

    // ECompressType - CCompressFactory::CreateCompress
    public const uint CompressNone = 0;
    public const uint CompressZlib = 1;
    public const uint CompressZstd = 5;

    public static string UnitTypeName(uint t) => t switch
    {
        UnitPatch => "PATCH",
        UnitMove => "MOVE",
        UnitNew => "NEW",
        UnitSetData => "SETDATA",
        _ => $"0x{t:X8}",
    };

    public static string InstallTypeName(uint t) => t switch
    {
        InstallRaw => "CInstallRAW",
        InstallFull => "CInstallFull",
        InstallAbi => "CInstallAbi",
        InstallFs => "CInstallFS",
        InstallExt4 => "CInstallext4",
        InstallBootp => "CInstallBootp",
        InstallDummy => "CInstallDummy",
        InstallBootpDummy => "CInstallBootpDummy",
        InstallGpt => "CInstallGPT",
        InstallPartitionControl => "CInstallPartitionControl",
        InstallSuperMetadata => "CInstallSuperMetadata",
        InstallSuperMetadataDummy => "CInstallSuperMetadataDummy",
        InstallUbi => "CInstallUbi",
        InstallSubFirmware => "CInstallSubFirmware",
        InstallYaffs2 => "CInstallYaffs2",
        _ => $"0x{t:X8}",
    };

    public static string CompressTypeName(uint t) => t switch
    {
        CompressNone => "none",
        CompressZlib => "zlib",
        CompressZstd => "zstd",
        _ => $"0x{t:X}",
    };
}
