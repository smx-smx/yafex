using System;
using System.Collections.Generic;
using System.Text;

namespace Smx.Yafex.FileFormats.Epk
{
	public enum PakBuildMode : UInt32
	{
		RELEASE = 0,
		DEBUG,
		TEST,
		UNKNOWN
	}
}
