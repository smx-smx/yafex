using Yafex.Support;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Yafex.FileFormats.FreescaleNand
{
	class FreescaleNand
	{
		private readonly mx28_nand_fcb fcb;
		private readonly IDataSource ds;
		
		public FreescaleNand(IDataSource ds) {
			var data = ds.Data;
			this.fcb = data.ReadStruct<mx28_nand_fcb>(0xC);
			this.ds = ds;
		}

		private int SectorOffset(int sector) {
			return (int)((uint)sector * fcb.total_page_size);
		}

		public IEnumerable<byte> GetPages(int index, int count) {
			var start = (uint)index * fcb.page_data_size;
			var end = start + (count * fcb.page_data_size);

			while (start < end) {
				var page = ds.Data.Span
					.Slice((int)start, (int)fcb.page_data_size)
					.ToArray();

				foreach (var b in page) {
					yield return b;
				}
				start += fcb.total_page_size;
			}
		}

		public IEnumerable<byte> GetFirmware1Data() {
			return GetPages(
				(int)fcb.firmware1_starting_sector,
				(int)fcb.sectors_in_firmware1
			);
		}

		public IEnumerable<byte> GetFirmware2Data() {
			return GetPages(
				(int)fcb.firmware2_starting_sector,
				(int)fcb.sectors_in_firmware2
			);
		}
	}

	public class FreescaleNandExtractor : IFormatExtractor
	{
		public IEnumerable<IDataSource> Extract(IDataSource source) {
			var nand = new FreescaleNand(source);

			// $DEBUG
			/*
			File.WriteAllBytes("C:/temp/fw1.bin", nand.GetFirmware1Data().ToArray());
			File.WriteAllBytes("C:/temp/fw2.bin", nand.GetFirmware2Data().ToArray());
			*/

			return Enumerable.Empty<IDataSource>();
		}
	}
}