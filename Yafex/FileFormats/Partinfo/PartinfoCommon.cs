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
using Smx.SharpIO;
using Smx.SharpIO.Memory.Buffers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Yafex.Support;

namespace Yafex.FileFormats.Partinfo
{
    public class PartinfoDevice
    {
        // Set by every From* factory via object initializer.
        public string name { get; private set; } = null!;
        public UInt64 size { get; private set; }
        public UInt64 phys { get; private set; }
        public UInt32 virt { get; private set; }
        public UInt32 cached { get; private set; }
        public UInt32 bandwidth { get; private set; }
        public bool used { get; private set; }

        private PartinfoDevice() { }

        public static PartinfoDevice FromMtdInfo(MtdInfo.DeviceInfo device)
        {
            return new PartinfoDevice()
            {
                name = new SpanStream(device.name).ReadCString(),
                size = device.size,
                phys = device.phys,
                virt = device.virt,
                cached = device.cached,
                used = device.used == 1
            };
        }

        public static PartinfoDevice FromPartinfoV1(PartinfoV1.DeviceInfo device)
        {
            // V1 (20110620) device records carry no used flag.
            return new PartinfoDevice()
            {
                name = new SpanStream(device.name).ReadCString(),
                size = device.size,
                phys = device.phys,
                virt = device.virt,
                cached = device.cached,
                used = false
            };
        }

        public static PartinfoDevice FromPartinfoV2(PartinfoV2.DeviceInfo device)
        {
            return new PartinfoDevice()
            {
                name = new SpanStream(device.name).ReadCString(),
                size = device.size,
                phys = device.phys,
                virt = device.virt,
                cached = device.cached,
                used = device.used == 1
            };
        }

        public static PartinfoDevice FromPartinfoV3(PartinfoV3.DeviceInfo device)
        {
            return new PartinfoDevice()
            {
                name = new SpanStream(device.name).ReadCString(),
                size = device.size,
                phys = device.phys,
                virt = device.virt,
                cached = device.cached,
                used = device.used == 1
            };
        }
    }

    public class PartinfoPartition
    {
        // Set by every From* factory via object initializer.
        public string name { get; private set; } = null!;
        public UInt64 offset { get; private set; }
        public UInt64 size { get; private set; }
        public string filename { get; private set; } = null!;
        public UInt32 filesize { get; private set; }
        public UInt32 sw_ver { get; private set; }
        public bool used { get; private set; }
        public bool valid { get; private set; }
        public PartinfoPartFlags mask_flags { get; private set; }
        // Spare word in newer variants (0 in older ones).
        public UInt32 extra { get; private set; }

        private PartinfoPartition() { }

        public static PartinfoPartition FromMtdInfo(MtdInfo.PartitionInfo part)
        {
            return new PartinfoPartition()
            {
                name = new SpanStream(part.name).ReadCString(),
                offset = part.offset,
                size = part.size,
                filename = new SpanStream(part.filename).ReadCString(),
                filesize = part.filesize,
                sw_ver = part.sw_ver,
                used = part.used == 1,
                valid = part.valid == 1,
                mask_flags = (PartinfoPartFlags)part.mask_flags,
                extra = 0
            };
        }
        public static PartinfoPartition FromPartinfoV1(PartinfoV1.PartitionInfo part)
        {
            return new PartinfoPartition()
            {
                name = new SpanStream(part.name).ReadCString(),
                offset = part.offset,
                size = part.size,
                filename = new SpanStream(part.filename).ReadCString(),
                filesize = part.filesize,
                sw_ver = part.sw_ver,
                used = part.used == 1,
                valid = part.valid == 1,
                mask_flags = (PartinfoPartFlags)part.mask_flags,
                extra = part.extra
            };
        }
        public static PartinfoPartition FromPartinfoV2(PartinfoV2.PartitionInfo part)
        {
            return new PartinfoPartition()
            {
                name = new SpanStream(part.name).ReadCString(),
                offset = part.offset,
                size = part.size,
                filename = new SpanStream(part.filename).ReadCString(),
                filesize = part.filesize,
                sw_ver = part.sw_ver,
                used = part.used == 1,
                valid = part.valid == 1,
                mask_flags = (PartinfoPartFlags)part.mask_flags,
                extra = 0
            };
        }
        public static PartinfoPartition FromPartinfoV3(PartinfoV3.PartitionInfo part)
        {
            return new PartinfoPartition()
            {
                name = new SpanStream(part.name).ReadCString(),
                offset = part.offset,
                size = part.size,
                filename = new SpanStream(part.filename).ReadCString(),
                filesize = part.filesize,
                sw_ver = part.sw_ver,
                used = part.used == 1,
                valid = part.valid == 1,
                mask_flags = (PartinfoPartFlags)part.mask_flags,
                extra = 0
            };
        }
    }

    public class PartinfoTable
    {
        public PartinfoType type;
        public UInt32 cur_epk_ver;
        public UInt32 old_epk_ver;
        public byte nmap;
        public byte npartition;
        public IList<PartinfoDevice> devices = new List<PartinfoDevice>();
        public IList<PartinfoPartition> partitions = new List<PartinfoPartition>();

        private PartinfoTable() { }

        public static PartinfoTable Parse(Smx.SharpIO.Memory.Buffers.ReadOnlySpan64<byte> data, PartinfoType type)
        {
            switch (type)
            {
                case PartinfoType.MtdInfo:
                    return FromMtdInfo(data.ReadStruct<MtdInfo.PartmapInfo>());
                case PartinfoType.PartinfoV1:
                    return FromPartinfoV1(data);
                case PartinfoType.PartinfoV2:
                    return FromPartinfoV2(data.ReadStruct<PartinfoV2.PartmapInfo>());
                case PartinfoType.PartinfoV3:
                    return FromPartinfoV3(data.ReadStruct<PartinfoV3.PartmapInfo>());
                case PartinfoType.MtdinfoFile:
                    return FromMtdinfoFile(data);
                default:
                    throw new NotSupportedException("Unsupported partinfo type");
            }
        }

        public static PartinfoTable FromMtdInfo(MtdInfo.PartmapInfo table)
        {
            return new PartinfoTable()
            {
                type = PartinfoType.MtdInfo,
                cur_epk_ver = table.cur_epk_ver,
                old_epk_ver = table.old_epk_ver,
                nmap = table.nmap,
                npartition = table.npartition,
                devices = table.dev.Select(d => PartinfoDevice.FromMtdInfo(d)).ToList(),
                partitions = table.partition.Select(p => PartinfoPartition.FromMtdInfo(p)).ToList()
            };
        }

        // V1 (20110620): u32 partition count, 88-byte entries.
        public static PartinfoTable FromPartinfoV1(ReadOnlySpan64<byte> data)
        {
            var header = data.ReadStruct<PartinfoV1.PartmapInfo>();
            long entryBase = Marshal.SizeOf<PartinfoV1.PartmapInfo>();
            long stride = Marshal.SizeOf<PartinfoV1.PartitionInfo>();
            long slots = (data.Length - entryBase) / stride;
            long count = Math.Min((long)header.npartition, slots);

            var partitions = new List<PartinfoPartition>();
            for (long i = 0; i < count; i++)
            {
                var entry = data.ReadStruct<PartinfoV1.PartitionInfo>(entryBase + i * stride);
                partitions.Add(PartinfoPartition.FromPartinfoV1(entry));
            }

            return new PartinfoTable()
            {
                type = PartinfoType.PartinfoV1,
                cur_epk_ver = header.cur_epk_ver,
                old_epk_ver = header.old_epk_ver,
                nmap = 0,
                npartition = (byte)Math.Min(count, 255),
                devices = new List<PartinfoDevice> { PartinfoDevice.FromPartinfoV1(header.dev) },
                partitions = partitions
            };
        }

        public static PartinfoTable FromPartinfoV2(PartinfoV2.PartmapInfo table)
        {
            return new PartinfoTable()
            {
                type = PartinfoType.PartinfoV2,
                cur_epk_ver = table.cur_epk_ver,
                old_epk_ver = table.old_epk_ver,
                nmap = 0,
                npartition = table.npartition,
                devices = new List<PartinfoDevice> { PartinfoDevice.FromPartinfoV2(table.dev) },
                partitions = table.partition.Select(p => PartinfoPartition.FromPartinfoV2(p)).ToList()
            };
        }
        public static PartinfoTable FromPartinfoV3(PartinfoV3.PartmapInfo table)
        {
            return new PartinfoTable()
            {
                type = PartinfoType.PartinfoV3,
                cur_epk_ver = table.cur_epk_ver,
                old_epk_ver = table.old_epk_ver,
                nmap = 0,
                npartition = table.npartition,
                devices = new List<PartinfoDevice> { PartinfoDevice.FromPartinfoV3(table.dev) },
                partitions = table.partition.Select(p => PartinfoPartition.FromPartinfoV3(p)).ToList()
            };
        }

        // mtdi.pak device-map files: header + 84-byte entries at 0xF0.
        public static PartinfoTable FromMtdinfoFile(ReadOnlySpan64<byte> data)
        {
            var header = data.ReadStruct<MtdinfoFile.PartmapInfo>();
            long slots = (data.Length - MtdinfoFile.Layout.EntriesOffset) / MtdinfoFile.Layout.EntryStride;
            long count = Math.Min(header.npartition, slots);

            var partitions = new List<PartinfoPartition>();
            for (long i = 0; i < count; i++)
            {
                long off = MtdinfoFile.Layout.EntriesOffset + i * MtdinfoFile.Layout.EntryStride;
                var entry = data.ReadStruct<MtdInfo.PartitionInfo>(off);
                partitions.Add(PartinfoPartition.FromMtdInfo(entry));
            }

            return new PartinfoTable()
            {
                type = PartinfoType.MtdinfoFile,
                cur_epk_ver = 0,
                old_epk_ver = 0,
                nmap = header.nmap,
                npartition = (byte)Math.Min(count, 255),
                devices = new List<PartinfoDevice> { PartinfoDevice.FromMtdInfo(header.dev) },
                partitions = partitions
            };
        }
    }
}
