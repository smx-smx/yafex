using System;
using System.Text;
using System.Runtime.CompilerServices;

using Yafex.Support;

using System.Runtime.InteropServices;

namespace Yafex.FileFormats.SddlSec
{
    [InlineArray(4)]
    public struct HeaderMagicBytes
    {
        private byte _element0;
    }

    [InlineArray(4)]
    public struct InfoEntriesCountStrBytes
    {
        private byte _element0;
    }

    [InlineArray(4)]
    public struct ModuleEntriesCountStrBytes
    {
        private byte _element0;
    }

    [InlineArray(16)]
    public struct Unused
    {
        private byte _element0;
    }

    public struct SddlSecHeader
    {
        public static byte[] SDDL_SEC_HEADER_MAGIC = [0x11, 0x22, 0x33, 0x44];

        public HeaderMagicBytes _headerMagic;
        public uint Unused;
        public InfoEntriesCountStrBytes _infoEntriesCountStrBytes;
        public ModuleEntriesCountStrBytes _moduleEntriesCountStrBytes;
        public Unused Unused2;

        public byte[] HeaderMagic => ((ReadOnlySpan<byte>)_headerMagic).ToArray();

        public uint InfoEntriesCount => Convert.ToUInt32(_infoEntriesCountStrBytes.AsString(Encoding.ASCII));
        public uint ModuleEntriesCount => Convert.ToUInt32(_moduleEntriesCountStrBytes.AsString(Encoding.ASCII));
        public uint TotalEntriesCount => 1 + InfoEntriesCount + ModuleEntriesCount; //1 is SDIT.FDI
    }

    [InlineArray(12)]
    public struct FileNameStrBytes
    {
        private byte _element0;
    }

    [InlineArray(12)]
    public struct FileSizeStrBytes
    {
        private byte _element0;
    }

    public struct EntryHeader
    {
        public static long PADDED_SIZE = 0x20;

        public FileNameStrBytes _fileNameStrBytes;
        public FileSizeStrBytes _fileSizeStrBytes;

        public string FileName => _fileNameStrBytes.AsString(Encoding.ASCII).Split('\0')[0];
        public uint FileSize => Convert.ToUInt32(_fileSizeStrBytes.AsString(Encoding.ASCII));
    }


    // SDIT
    [InlineArray(4)]
    public struct Version
    {
        private byte _element0;
    }
	
    public struct SditHeader
    {
        public HeaderMagicBytes _headerMagic;
        public byte GroupCount;
        private byte _unknown1;
        private ushort _unknown2; 

        public byte[] HeaderMagic => ((ReadOnlySpan<byte>)_headerMagic).ToArray();
    }
	
    public struct SditGroupHeader
    {
        public byte GroupID;
        public byte EntryCount;
        private ushort _unknown;
    }

    [InlineArray(4)]
    public struct IdBlock
    {
        private byte _element0;
    }

    [InlineArray(8)]
    public struct Unknown
    {
        private byte _element0;
    }

    [InlineArray(8)]
    public struct ModuleName
    {
        private byte _element0;
    }

    public struct SditModuleEntry
    {
        private IdBlock _idBlock;
        private Unknown _groupUnique;
        public Version BaseVersion;
        public Version PreviousVersion;
        public Version Version;
        public byte ModuleType;
        private byte _unknown2;
        public ushort SegmentCount;
        private Unknown _unused;
        private ModuleName _moduleNameBytes;

        public string ModuleName => _moduleNameBytes.AsString(Encoding.ASCII);
        public string VersionString => string.Format("{0}.{1}{2}{3}",
                                            Version[0], Version[1],
                                            Version[2], Version[3]);
    }
	

    //Module data
    public struct ModuleHeader
    {
        public HeaderMagicBytes _headerMagic;
        private IdBlock _idBlock;
        private Unknown _unused;
        public Version BaseVersion;
        public Version PreviousVersion;
        public Version Version;
        private uint _unknown1;
        public ushort SegmentIndex;
        public byte Flags;
        public byte ModuleType;
        public uint StoredDataSize;
        public uint UncompressedDataSize;
        public uint Checksum;
        
        public byte[] HeaderMagic => ((ReadOnlySpan<byte>)_headerMagic).ToArray();

        public bool isCompressed => (Flags & 0x01) != 0;
        public bool isCiphered => (Flags & 0x02) != 0;
        public string VersionString => string.Format("{0}.{1}{2}{3}",
                                            Version[0], Version[1],
                                            Version[2], Version[3]);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ContentHeader
    {
        private byte _magic1; //0x01
        public uint _destOffset;
        public uint _sourceOffset;
        public uint Size;
        private byte _magic2; //0x21

        public uint DestOffset => (((_destOffset >> 28) & 0xF) == 0xD) ? _destOffset &= 0x0FFFFFFF : _destOffset;
        public uint SourceOffset => (((_sourceOffset >> 28) & 0xF) == 0xC) ? _sourceOffset &= 0x0FFFFFFF : _sourceOffset;
        public bool hasSubFile => SourceOffset == 0x10E;
    }
}