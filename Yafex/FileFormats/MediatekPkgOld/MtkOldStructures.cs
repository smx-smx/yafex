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
using System.Runtime.InteropServices;
using System.Text;

using Yafex.Support;

// Binary-layout structs populated via ReadStruct/Marshal: padding and
// reserved fields are never assigned/read in C# by design (CS0649/CS0169).
#pragma warning disable CS0649, CS0169

namespace Yafex.FileFormats.MediatekPkgOld;

public struct MtkOldHeader
{
    public const string MTK_MAGIC = "#DH@FiRm";
    public const int SIZE = 0x98;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    private byte[] vendorMagic;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    private byte[] mtkMagic;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 68)]
    private byte[] version;
    public uint fileSize;
    public uint flags;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    private byte[] productName;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    private byte[] digest;

    public string VendorMagic => vendorMagic.AsString(Encoding.ASCII);
    public string MtkMagic => mtkMagic.AsString(Encoding.ASCII);
    public string Version => version.AsString(Encoding.ASCII);
    public string ProductName => productName.AsString(Encoding.ASCII);
    public byte[] VendorMagicBytes => (byte[])vendorMagic.Clone();
}
