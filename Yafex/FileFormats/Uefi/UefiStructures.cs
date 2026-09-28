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
using System.Collections.Generic;

namespace Yafex.FileFormats.Uefi;

/// <summary>
/// UEFI firmware volume / FFS / section structure shapes and well-known
/// constants (UEFI PI specification layout). Parsing lives in
/// <see cref="Uefi"/>; detection/extraction in <c>UefiFormat</c>.
/// </summary>
public static class UefiStructures
{
    public const uint FlashDescriptorSignature = 0x0FF0A55A;
    public const int FlashDescriptorOffset = 0;
    public const int FlashDescriptorSigOffset = 0x10;

    public static readonly Guid FsFfs1 = new("7a9354d9-0468-444a-81ce-0bf617d890df");
    public static readonly Guid FsFfs2 = new("8c8ce578-8a3d-4f1c-9935-896185c32dd3");
    public static readonly Guid FsFfs3 = new("5473c07a-3dcb-4dca-bd6f-1e9689e7349a");
    public static readonly Guid FsNvramEvsa = new("fff12b8d-7696-4c8b-a985-2747075b4f50");
    public static readonly Guid FsNvramNvar = new("cef5b9a3-476d-497f-9fdc-e98143e0422c");

    public static bool IsFfsFileSystem(Guid g) => g == FsFfs1 || g == FsFfs2 || g == FsFfs3;

    // EFI_GUID_DEFINED_SECTION definition GUIDs (decompression dispatch).
    public static readonly Guid GuidedLzma = new("ee4e5898-3914-4259-9d6e-dc7bd79403cf");
    public static readonly Guid GuidedLzmaHp = new("0ed85e23-f253-413f-a03c-901987b04397");
    public static readonly Guid GuidedTiano = new("a31280ad-481e-41b6-95e8-127f4c984779");
    public static readonly Guid GuidedGzipQc = new("1d301fe9-be79-4353-91c2-d23bc959ae0c");
    public static readonly Guid GuidedZlibAmd = new("ce3233f5-2cd6-4d87-9152-4a238bb6d1c4");
    public static readonly Guid GuidedFvImage = new("24400798-3807-4a42-b413-a1ecee205dd8");
    // Uncompressed wrapper: content is used as-is (sections or FV image).
    public static readonly Guid GuidedStatic = new("fc1bcdb0-7d31-49aa-936a-a4600d9dd083");

    public static readonly Dictionary<byte, string> FileTypes = new()
    {
        [0x00] = "ALL", [0x01] = "RAW", [0x02] = "FREEFORM",
        [0x03] = "SEC_CORE", [0x04] = "PEI_CORE", [0x05] = "DXE_CORE",
        [0x06] = "PEIM", [0x07] = "DRIVER", [0x08] = "COMBINED_PEIM_DRIVER",
        [0x09] = "APPLICATION", [0x0A] = "MM", [0x0B] = "FV_IMAGE",
        [0x0C] = "COMBINED_MM_DXE", [0x0D] = "MM_CORE", [0x0E] = "MM_STANDALONE",
        [0x0F] = "MM_CORE_STANDALONE", [0xF0] = "PAD",
    };

    // Note: Insyde H2O images carry a proprietary 0x20 section (8 bytes,
    // typically right before TE); unknown types surface as SEC_xx leaves.
    public static readonly Dictionary<byte, string> SectionTypes = new()
    {
        [0x01] = "COMPRESS", [0x02] = "GUID_DEFINED", [0x03] = "DISPOSABLE",
        [0x10] = "PE32", [0x11] = "PIC", [0x12] = "TE",
        [0x13] = "DXE_DEPEX", [0x14] = "VERSION", [0x15] = "UI",
        [0x16] = "COMPAT", [0x17] = "FV_IMAGE", [0x18] = "SUBTYPE_GUID",
        [0x19] = "RAW", [0x1B] = "PEI_DEPEX", [0x1C] = "MM_DEPEX",
    };

    public static readonly Dictionary<int, string> RegionNames = new()
    {
        [0] = "fd", [1] = "bios", [2] = "me", [3] = "gbe", [4] = "pdr",
    };

    public static readonly Dictionary<byte, string> FitTypes = new()
    {
        [0x00] = "FIT_HEADER", [0x01] = "MICROCODE", [0x02] = "BIOS_ACM",
        [0x07] = "BIOS_INIT", [0x08] = "TPM_POLICY", [0x09] = "BIOS_POLICY",
        [0x0A] = "TXT_POLICY", [0x0B] = "KEY_MANIFEST", [0x0C] = "BOOT_POLICY",
        [0x7F] = "FIT_HEADER",
    };

    public static string FileTypeName(byte t) =>
        FileTypes.TryGetValue(t, out var n) ? n : $"TYPE_{t:X2}";

    public static string SectionTypeName(byte t) =>
        SectionTypes.TryGetValue(t, out var n) ? n : $"SEC_{t:X2}";

    public static string RegionName(int i) =>
        RegionNames.TryGetValue(i, out var n) ? n : $"r{i}";

    public static string FitTypeName(byte t) =>
        FitTypes.TryGetValue(t, out var n) ? n : $"FIT_{t:X2}";

    /// <summary>First 8 hex digits of a GUID (compact artifact names).</summary>
    public static string ShortGuid(Guid g) => g.ToString("N")[..8];

    /// <summary>Filename-safe UI string (letters/digits/dash/dot kept).</summary>
    public static string SanitizeUi(string ui)
    {
        var chars = new char[Math.Min(ui.Length, 32)];
        int n = 0;
        foreach (char c in ui)
        {
            if (n >= chars.Length)
            {
                break;
            }
            chars[n++] = char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '_' ? c : '_';
        }
        return new string(chars, 0, n).Trim('_', '.');
    }
}
