﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Yafex.FileFormats.FreescaleNand
{
	public struct mx28_nand_timings
	{
		public byte data_setup;
		public byte data_hold;
		public byte address_setup;
		public byte dsample_time;
		public byte nand_timing_state;
		public byte rea;
		public byte rloh;
		public byte rhoh;
	}

	public struct mx28_nand_fcb
	{
		public uint checksum;
		public uint fingerprint;
		public uint version;
		public mx28_nand_timings timing;
		public uint page_data_size;
		public uint total_page_size;
		public uint sectors_per_block;
		public uint number_of_nands;       /* Ignored */
		public uint total_internal_die;        /* Ignored */
		public uint cell_type;         /* Ignored */
		public uint ecc_block_n_ecc_type;
		public uint ecc_block_0_size;
		public uint ecc_block_n_size;
		public uint ecc_block_0_ecc_type;
		public uint metadata_bytes;
		public uint num_ecc_blocks_per_page;
		public uint ecc_block_n_ecc_level_sdk; /* Ignored */
		public uint ecc_block_0_size_sdk;      /* Ignored */
		public uint ecc_block_n_size_sdk;      /* Ignored */
		public uint ecc_block_0_ecc_level_sdk; /* Ignored */
		public uint num_ecc_blocks_per_page_sdk;   /* Ignored */
		public uint metadata_bytes_sdk;        /* Ignored */
		public uint erase_threshold;
		public uint boot_patch;
		public uint patch_sectors;
		public uint firmware1_starting_sector;
		public uint firmware2_starting_sector;
		public uint sectors_in_firmware1;
		public uint sectors_in_firmware2;
		public uint dbbt_search_area_start_address;
		public uint badblock_marker_byte;
		public uint badblock_marker_start_bit;
		public uint bb_marker_physical_offset;
	};

	public struct mx28_nand_dbbt
	{
		public uint checksum;
		public uint fingerprint;
		public uint version;
		public uint number_bb;
		public uint number_2k_pages_bb;
	};

	public struct mx28_nand_bbt
	{
		public uint nand;
		public uint number_bb;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 510)]
		public uint[] badblock;
	};
}
