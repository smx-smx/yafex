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
using System.Collections.Generic;

namespace Yafex.FileFormats.LgUp;

/// <summary>In-memory model of an LGUP package. Ported from the elix lgup tool.</summary>
public sealed class LgUpRangeset
{
    public List<(ulong Offset, ulong Size)> Ranges = new();
    public ulong TotalSize
    {
        get
        {
            ulong total = 0;
            foreach (var r in Ranges) total += r.Size;
            return total;
        }
    }
}

public sealed class LgUpUnit
{
    public uint UpdateType;
    public LgUpRangeset[] Rangesets = new LgUpRangeset[5];
    public byte[] SourceMd5 = [];
    public byte[] TargetMd5 = [];
    public byte[] BackupMd5 = [];
    public uint PatchSize;        // uiPatchSize
    public uint UncompressedSize; // nonzero => payload is compressed
    public byte[] Sha256 = [];

    // CInstallFull units only
    public uint FullUnitSize;
    public string FullTargetName = "";

    // extraction results
    public long FileOffset;  // offset of the encrypted payload in the file
    public byte[] Payload = []; // encrypted payload as stored
    public byte[] Content = []; // decrypted payload (padding stripped)
    public byte[] Output = [];  // decompressed content (or Content when uncompressed)

    public LgUpUnit()
    {
        for (int i = 0; i < Rangesets.Length; i++) Rangesets[i] = new LgUpRangeset();
    }

    public override string ToString() =>
        $"{LgUpFormat.UnitTypeName(UpdateType)} patch={PatchSize} flag={UncompressedSize}";
}

public sealed class LgUpSection
{
    public uint InstallType;
    public string Name = "";
    // CInstallext4
    public ulong SourceSize;
    public ulong TargetSize;
    public uint KeyUnit;
    public uint TotalUnits;
    // CInstallFull
    public string Name2 = "";
    public ulong FullSize;
    public uint FullFlag;
    public uint FullCount;

    public List<LgUpUnit> Units = new();
    public byte[] RawHeader = [];
}

public sealed class LgUpPackage
{
    public byte[] Signature = [];  // file[0..0x100)
    public uint EncHeaderSize;     // u32 @0x100 (size incl. magic, excl. prefix)
    public string OuterMagic = ""; // EPKG | LGUA
    public uint LguaSize;
    public uint ExecSize;          // LGUA embedded executable size
    public byte[] Exec = [];
    public long PackageStart;      // file offset of the inner EPKG package
    public byte[] EncryptedHeader = [];
    public byte[] DecryptedHeader = [];
    public uint HeaderSize;        // decrypted[0]
    public int Version;            // 4 or 5
    public uint CompressType;
    public uint TotalDataSize;
    public uint SectionCount;
    public uint[] SectionHeaderSizes = [];
    public uint[] PatchOffsets = [];
    public List<LgUpSection> Sections = new();
    public long DataBase;          // file offset of the patch data region
    public long TrailerOffset;     // file offset where the LGUP data region ends
}
