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

namespace Yafex.FileFormats.Partinfo
{
    public enum PartinfoType
    {
        MtdInfo,
        PartinfoV1,
        PartinfoV2,
        Unknown
    }

    [Flags]
    public enum PartinfoPartFlags : uint
    {
        Fixed = 1 << 0,
        Master = 1 << 1,
        IdKey = 1 << 2,
        Cache = 1 << 3,
        Data = 1 << 4,
        Secured = 1 << 5,
        Erase = 1 << 6
    }

    namespace MtdInfo
    {
        public struct DeviceInfo
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] name;
            public UInt32 size;
            public UInt32 phys;
            public UInt32 virt;
            public UInt32 cached;
            public UInt32 bandwidth;
            public UInt32 used;
        }

        public struct PartitionInfo
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] name;
            public UInt32 offset;
            public UInt32 size;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] filename;
            public UInt32 filesize;
            public UInt32 sw_ver;
            public byte used;
            public byte valid;
            public byte mask_flags;
        }

        public struct PartmapInfo
        {
            public const uint MAGIC = 0x20081118;

            public UInt32 magic;
            public UInt32 cur_epk_ver;
            public UInt32 old_epk_ver;
            public byte nmap;
            public byte npartition;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public DeviceInfo[] dev;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public PartitionInfo[] partition;
        }
    }

    namespace PartinfoV1
    {
        public struct DeviceInfo
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] name;
            public UInt32 size;
            public UInt32 phys;
            public UInt32 virt;
            public UInt32 cached;
            public UInt32 bandwidth;
            public UInt32 used;
        }

        public struct PartitionInfo
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] name;
            public UInt32 offset;
            public UInt32 size;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] filename;
            public UInt32 filesize;
            public UInt32 sw_ver;
            public byte used;
            public byte valid;
            public UInt32 mask_flags;
        }

        public struct PartmapInfo
        {
            public const uint MAGIC = 0x20110729;

            public UInt32 magic;
            public UInt32 cur_epk_ver;
            public UInt32 old_epk_ver;
            public byte npartition;
            public DeviceInfo dev;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public PartitionInfo[] partition;
        }

    }

    namespace PartinfoV2
    {
        public struct DeviceInfo
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] name;
            public UInt64 size;
            public UInt64 phys;
            public UInt32 virt;
            public UInt32 cached;
            public UInt32 bandwidth;
            public UInt32 used;
        }

        public struct PartitionInfo
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] name;
            public UInt64 offset;
            public UInt64 size;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] filename;
            public UInt32 filesize;
            public UInt32 sw_ver;
            public byte used;
            public byte valid;
            public UInt32 mask_flags;
        }

        public struct PartmapInfo
        {
            public const uint MAGIC = 0x20120716;

            public UInt32 magic;
            public UInt32 cur_epk_ver;
            public UInt32 old_epk_ver;
            public byte npartition;
            public DeviceInfo dev;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            public PartitionInfo[] partition;
        }
    }
}
