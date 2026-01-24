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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Yafex.FileFormats.EpkV1
{
	[StructLayout(LayoutKind.Sequential)]
	public struct Epk1BeHeader
	{
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		public byte[] epkMagic;
		private UInt32 fileSize;
		private UInt32 pakCount;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
		public PakRec[] pakRecs;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		private byte[] fwVer;

		public string EpakVersion => string.Format("{0:X2}.{1:X2}.{2:X2}.{3:X2}",
											fwVer[0], fwVer[1],
											fwVer[2], fwVer[3]);

		public UInt32 FileSize => fileSize.BigEndianToHost();
		public UInt32 PakCount => pakCount.BigEndianToHost();
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct PakRec
	{
		public UInt32 offset;
		public UInt32 size;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct PakHeader
	{
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		private byte[] imageType;
		public UInt32 imageSize;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
		private byte[] modelName;
		public UInt32 swVersion;
		public UInt32 swDate;
		public PakBuildMode devMode;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 44)]
		private byte[] reserved;

		public string PakName => imageType.AsString(Encoding.ASCII);

		public string Platform => modelName.AsString(Encoding.ASCII);

	}

	[StructLayout(LayoutKind.Sequential)]
	public struct Epk1Header
	{
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		private byte[] epakMagic;
		public UInt32 fileSize;
		public UInt32 pakCount;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
		public PakRec[] pakRecs;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		private byte[] fwVer;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
		private byte[] otaID;

		public string EpakMagic => epakMagic.AsString(Encoding.ASCII);
		public string OtaID => otaID.AsString(Encoding.ASCII);
		public string EpakVersion => string.Format("{0:X2}.{1:X2}.{2:X2}.{3:X2}",
													fwVer[3], fwVer[2],
													fwVer[1], fwVer[0]);
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct Epk1HeaderNew
	{
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		public byte[] epakMagic;
		public UInt32 fileSize;
		public UInt32 pakCount;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		public byte[] fwVer;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
		public byte[] otaID;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 26)]
		public PakRec[] pakRecs;
	}
}
