using Yafex.FileFormats.Nfwb;
using Yafex.Support;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Runtime.CompilerServices;

namespace Yafex.FileFormats.Nfwb
{
    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public unsafe struct Header
    {
        private fixed byte nfwbMagic[4];
        private UInt32 versionMajor;
        private UInt32 versionMinor;
        private UInt32 unused;

        [InlineArray(16)]
        public struct FirmwareName { private byte _element0; }
        public FirmwareName firmwareName;

        public UInt32 dataSize;

        [InlineArray(16)]
        public struct MD5Checksum { private byte _element0; }
        public MD5Checksum md5Checksum; //of dataSize after this header

        public UInt32 partCount;
        public UInt32 headerSize;

        [InlineArray(128)]
        public struct Signature { private byte _element0; }
        public Signature signature;

        public UInt32 headerChecksum; //CRC32, calculated with this field set to 0

        public string FirmwareVersion => string.Format("{0}.{1}", versionMajor, versionMinor);
        
        public byte[] FirmwareNameBytes => ((ReadOnlySpan<byte>)firmwareName).ToArray();
        public string FirmwareNameString => FirmwareNameBytes.AsString(Encoding.ASCII);
    
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct PartEntry
    {
        public UInt32 partitionID;
        public UInt32 size;
        public UInt32 offset;
        public fixed byte md5Checksum[16];

    } 
}