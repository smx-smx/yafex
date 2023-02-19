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
﻿using Yafex.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Yafex.FileFormats.EpkV3
{
	public struct EPK_V3_HEADER
	{
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		private byte[] epkMagic;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		private byte[] epakVersion;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
		private byte[] otaId;
		public UInt32 packageInfoSize;
		public UInt32 bChunked;

		public string EpkMagic => epkMagic.AsString(Encoding.ASCII);
		public string OtaId => otaId.AsString(Encoding.ASCII);
		public string EpkVersion => string.Format("{0:X2}.{1:X2}.{2:X2}.{3:X2}",
													epakVersion[3], epakVersion[2],
													epakVersion[1], epakVersion[0]);
	}

	public struct EPK_V3_NEW_HEADER
	{
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		private byte[] epkMagic;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		private byte[] epakVersion;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
		private byte[] otaId;
		public UInt32 packageInfoSize;
		public UInt32 bChunked;
		public UInt32 pakInfoMagic;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
		private byte[] encryptType;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
		private byte[] updateType;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 1399)]
		public byte[] reserved;

		public string EpkMagic => epkMagic.AsString(Encoding.ASCII);
		public string OtaId => otaId.AsString(Encoding.ASCII);
		public string EpkVersion => string.Format("{0:X2}.{1:X2}.{2:X2}.{3:X2}",
													epakVersion[3], epakVersion[2],
													epakVersion[1], epakVersion[0]);
		
		public string EncryptType => encryptType.AsString(Encoding.ASCII);
		public string UpdaetType => updateType.AsString(Encoding.ASCII);
	}

	public struct PACKAGE_SEGMENT_INFO
	{
		public UInt32 isSegmented;
		public UInt32 segmentIndex;
		public UInt32 segmentCount;
		public UInt32 segmentSize;
	}

	public struct PACKAGE_INFO_DATA
	{
		public UInt32 reserved;
	}

	public struct PAK_V3_LISTHEADER
	{
		public UInt32 packageInfoListSize;
		public UInt32 packageInfoCount;
		// packages array follows
	}

	public struct PAK_V3_NEW_LISTHEADER
	{
		public UInt32 packageInfoListSize;
		public UInt32 packageInfoCount;
		public UInt32 pakInfoMagic;
		// packages array follows
	}

	public struct PAK_V3_HEADER
	{
		public UInt32 packageType;
		public UInt32 packageInfoSize;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
		public byte[] packageName;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 96)]
		public byte[] packageVersion;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
		public byte[] packageArchitecture;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
		public byte[] checkSum;
		public UInt32 packageSize;
		public UInt32 dipk;
		public PACKAGE_SEGMENT_INFO segmentInfo;
		public PACKAGE_INFO_DATA infoData;
	}

	public struct PAK3_STRUCTURE
	{
		public const int SIGNATURE_SIZE = 0x80;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = SIGNATURE_SIZE)]
		public byte[] signature;
		public PAK_V3_HEADER header;
	}

	public struct EPK_V3_HEAD_STRUCTURE
	{
		public const int SIGNATURE_SIZE = 0x80;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = SIGNATURE_SIZE)]
		public byte[] signature;

		public EPK_V3_HEADER epkHeader;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 384)]
		public byte[] crc32Info;

		public UInt32 reserved;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
		public string platformVersion;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
		public string sdkVersion;
	}

	public struct EPK_V3_NEW_HEAD_STRUCTURE
	{
		public const int SIGNATURE_SIZE = 0x80;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = SIGNATURE_SIZE)]
		public byte[] signature;
		
		public EPK_V3_NEW_HEADER epkHeader;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
		public string platformVersion;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
		public string sdkVersion;

		public static ReadOnlySpan<T> GetHeader<T>(ReadOnlySpan<T> data) where T : unmanaged {
			return data.GetField<T, EPK_V3_NEW_HEAD_STRUCTURE, EPK_V3_NEW_HEADER>(nameof(epkHeader));
		}
	}

	public struct EPK_V3_NEW_STRUCTURE
	{
		public const int SIGNATURE_SIZE = 0x80;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = SIGNATURE_SIZE)]
		public byte[] signature;

		public EPK_V3_NEW_HEAD_STRUCTURE head;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = SIGNATURE_SIZE)]
		public byte[] packageInfo_signature;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = SIGNATURE_SIZE)]
		public byte[] sig2;

		public PAK_V3_NEW_LISTHEADER packageInfo;

		public static ReadOnlySpan<T> GetHead<T>(ReadOnlySpan<T> data) where T : unmanaged {
			return data.GetField<T, EPK_V3_NEW_STRUCTURE, EPK_V3_NEW_HEAD_STRUCTURE>(nameof(head));
		}
	}
}
