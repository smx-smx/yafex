#region License
/*
 * Copyright (c) 2023 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
﻿using Yafex.FileFormats.Epk;
using Yafex.Support;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Yafex.FileFormats.EpkV2
{
	[StructLayout(LayoutKind.Sequential)]
	public struct PAK_V2_LOCATION
	{
		public UInt32 ImageOffset;
		public UInt32 ImageSize; //containing headers (excluded signatures)
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		private byte[] imageType;
		public UInt32 ImageVersion;
		public UInt32 SegmentSize;

		public string ImageType => imageType.AsString(Encoding.ASCII);
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct PAK_V2_HEADER
	{
		public const string PAK_MAGIC = "MPAK";

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		private byte[] imageType;
		public UInt32 imageSize;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
		private byte[] modelData;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		public byte[] swVersion;
		public UInt32 swDate;
		public PakBuildMode devMode;
		public UInt32 segmentCount;
		public UInt32 segmentSize;
		public UInt32 segmentIndex;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		private byte[] pakMagic;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
		private byte[] reserved;
		public UInt32 segmentCrc32;

		public string ImageType => imageType.AsString(Encoding.ASCII);
		public string ModelName => modelData.AsString(Encoding.ASCII);

		public string PakMagic => pakMagic.AsString(Encoding.ASCII);
		public string SwVersion => string.Format("{0:X2}.{1:X2}.{2:X2}.{3:X2}",
													swVersion[3], swVersion[2],
													swVersion[1], swVersion[0]);
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct PAK_V2_STRUCTURE
	{
		public const int SIGNATURE_SIZE = 0x80;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = SIGNATURE_SIZE)]
		public byte[] signature;
		public PAK_V2_HEADER pakHeader;

		public static ReadOnlySpan<T> GetHeader<T>(Span<T> pak2) where T : unmanaged {
			return pak2.GetField<T, PAK_V2_STRUCTURE, PAK_V2_HEADER>(nameof(pakHeader));
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct EPK_V2_HEADER
	{
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		private byte[] fileType;
		public UInt32 fileSize;
		public UInt32 fileNum;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		private byte[] epkMagic;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		private byte[] epakVersion;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
		private byte[] otaId;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
		public PAK_V2_LOCATION[] imageLocations;

		public string FileType => fileType.AsString(Encoding.ASCII);
		public string EpkMagic => epkMagic.AsString(Encoding.ASCII);
		public string OtaId => otaId.AsString(Encoding.ASCII);
		public string EpkVersion => string.Format("{0:X2}.{1:X2}.{2:X2}.{3:X2}",
													epakVersion[3], epakVersion[2],
													epakVersion[1], epakVersion[0]);
	}

	[StructLayout(LayoutKind.Sequential, Pack = 0, CharSet = CharSet.Ansi)]
	public struct EPK_V2_STRUCTURE
	{
		public const int SIGNATURE_SIZE = 0x80;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = SIGNATURE_SIZE)]
		public byte[] signature;
		public EPK_V2_HEADER epkHeader;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
		public UInt32[] crc32Info;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
		public string platformVersion;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
		public string sdkVersion;

		public static ReadOnlySpan<T> GetHeader<T>(ReadOnlySpan<T> epk) where T : unmanaged {
			return epk.GetField<T, EPK_V2_STRUCTURE, EPK_V2_HEADER>(nameof(epkHeader));
		}
	}
}
