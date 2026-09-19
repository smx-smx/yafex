using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Yafex.FileFormats.LgUp;

public struct LgUpStructure
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x104)]
    public byte[] Header;
}

public class LgUpConstants
{
    public const string EPKG_MAGIC = "EPKG";
}