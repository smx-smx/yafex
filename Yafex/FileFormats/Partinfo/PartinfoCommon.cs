using Smx.Yafex.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Smx.Yafex.FileFormats.Partinfo
{
	public class PartinfoDevice
	{
		public string name { get; private set; }
		public UInt64 size { get; private set; }
		public UInt64 phys { get; private set; }
		public UInt32 virt { get; private set; }
		public UInt32 cached { get; private set; }
		public UInt32 bandwidth { get; private set; }
		public bool used { get; private set; }

		private PartinfoDevice() { }

		public static PartinfoDevice FromMtdInfo(MtdInfo.DeviceInfo device) {
			return new PartinfoDevice() {
				name = new SpanStream(device.name).ReadCString(),
				size = device.size,
				phys = device.phys,
				virt = device.virt,
				cached = device.cached,
				used = device.used == 1
			};
		}

		public static PartinfoDevice FromPartinfoV1(PartinfoV1.DeviceInfo device) {
			return new PartinfoDevice() {
				name = new SpanStream(device.name).ReadCString(),
				size = device.size,
				phys = device.phys,
				virt = device.virt,
				cached = device.cached,
				used = device.used == 1
			};
		}

		public static PartinfoDevice FromPartinfoV2(PartinfoV2.DeviceInfo device) {
			return new PartinfoDevice() {
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
		public string name { get; private set; }
		public UInt64 offset { get; private set; }
		public UInt64 size { get; private set; }
		public string filename { get; private set; }
		public UInt32 filesize { get; private set; }
		public UInt32 sw_ver { get; private set; }
		public bool used { get; private set; }
		public bool valid { get; private set; }
		public PartinfoPartFlags mask_flags { get; private set; }

		private PartinfoPartition() { }

		public static PartinfoPartition FromMtdInfo(MtdInfo.PartitionInfo part) {
			return new PartinfoPartition() {
				name = new SpanStream(part.name).ReadCString(),
				offset = part.offset,
				size = part.size,
				filename = new SpanStream(part.filename).ReadCString(),
				filesize = part.filesize,
				sw_ver = part.sw_ver,
				used = part.used == 1,
				valid = part.valid == 1,
				mask_flags = (PartinfoPartFlags) part.mask_flags
			};
		}
		public static PartinfoPartition FromPartinfoV1(PartinfoV1.PartitionInfo part) {
			return new PartinfoPartition() {
				name = new SpanStream(part.name).ReadCString(),
				offset = part.offset,
				size = part.size,
				filename = new SpanStream(part.filename).ReadCString(),
				filesize = part.filesize,
				sw_ver = part.sw_ver,
				used = part.used == 1,
				valid = part.valid == 1,
				mask_flags = (PartinfoPartFlags)part.mask_flags
			};
		}
		public static PartinfoPartition FromPartinfoV2(PartinfoV2.PartitionInfo part) {
			return new PartinfoPartition() {
				name = new SpanStream(part.name).ReadCString(),
				offset = part.offset,
				size = part.size,
				filename = new SpanStream(part.filename).ReadCString(),
				filesize = part.filesize,
				sw_ver = part.sw_ver,
				used = part.used == 1,
				valid = part.valid == 1,
				mask_flags = (PartinfoPartFlags)part.mask_flags
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

		public static PartinfoTable FromMtdInfo(MtdInfo.PartmapInfo table) {
			return new PartinfoTable() {
				type = PartinfoType.MtdInfo,
				cur_epk_ver = table.cur_epk_ver,
				old_epk_ver = table.old_epk_ver,
				nmap = table.nmap,
				npartition = table.npartition,
				devices = table.dev.Select(d => PartinfoDevice.FromMtdInfo(d)).ToList(),
				partitions = table.partition.Select(p => PartinfoPartition.FromMtdInfo(p)).ToList()
			};
		}
		public static PartinfoTable FromPartinfoV1(PartinfoV1.PartmapInfo table) {
			return new PartinfoTable() {
				type = PartinfoType.PartinfoV1,
				cur_epk_ver = table.cur_epk_ver,
				old_epk_ver = table.old_epk_ver,
				nmap = 0,
				npartition = table.npartition,
				devices = new List<PartinfoDevice> { PartinfoDevice.FromPartinfoV1(table.dev) },
				partitions = table.partition.Select(p => PartinfoPartition.FromPartinfoV1(p)).ToList()
			};
		}
		public static PartinfoTable FromPartinfoV2(PartinfoV2.PartmapInfo table) {
			return new PartinfoTable() {
				type = PartinfoType.PartinfoV2,
				cur_epk_ver = table.cur_epk_ver,
				old_epk_ver = table.old_epk_ver,
				nmap = 0,
				npartition = table.npartition,
				devices = new List<PartinfoDevice> { PartinfoDevice.FromPartinfoV2(table.dev) },
				partitions = table.partition.Select(p => PartinfoPartition.FromPartinfoV2(p)).ToList()
			};
		}
	}
}
