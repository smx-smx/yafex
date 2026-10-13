using System;
using System.IO;

namespace Yafex.FileSystems.Squashfs2
{
    internal static class Squashfs2Constants
    {
        public const uint Magic = 0x73717368;
        // Same magic, byte-swapped ("sqsh"): big-endian image.
        public const uint SwappedMagic = 0x68737173;
        public const int SuperblockSize = 119;
        public const ushort MajorVersion = 2;
        public const int MetadataSize = 8192;

        // Superblock field offsets (shared 3.x/v2 packed layout; v2 uses
        // the 32-bit _2 table aliases instead of the 64-bit fields).
        public const int InodesOff = 4;
        public const int BytesUsed2Off = 8;
        public const int UidStart2Off = 12;
        public const int GuidStart2Off = 16;
        public const int InodeTable2Off = 20;
        public const int DirTable2Off = 24;
        public const int MajorOff = 28;
        public const int MinorOff = 30;
        public const int BlockSize1Off = 32;
        public const int BlockLogOff = 34;
        public const int FlagsOff = 36;
        public const int NoUidsOff = 37;
        public const int NoGuidsOff = 38;
        public const int MkfsTimeOff = 39;
        // v2 shares the packed prefix; tables resolve through the 32-bit
        // _2 aliases, root keeps the generic split.
        public const int RootInodeOff = 43;
        public const int BlockSizeOff = 51;
        public const int FragmentsOff = 55;
        public const int FragmentTable2Off = 59;

        // Metablock c_byte: bit set means stored raw, clear means compressed.
        public const uint StoredBitMeta = 1u << 15;
        // Data block list entry: bit set means stored raw, else compressed.
        public const uint StoredBitBlock = 1u << 24;

        public const uint InvalidFragment = 0xFFFFFFFF;
        public const uint GuidSameAsUid = 255;

        // Inode types (v2 shares the 3.x numbering).
        public const int TypeDir = 1;
        public const int TypeReg = 2;
        public const int TypeSymlink = 3;
        public const int TypeBlkdev = 4;
        public const int TypeChrdev = 5;
        public const int TypeFifo = 6;
        public const int TypeSocket = 7;
        public const int TypeLdir = 8;
        public const int TypeLreg = 9;

        public static ushort ReadU16(byte[] buf, long off)
        {
            return (ushort)(buf[off] | (buf[off + 1] << 8));
        }

        public static uint ReadU32(byte[] buf, long off)
        {
            return (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));
        }

        public static ulong ReadU64(byte[] buf, long off)
        {
            return (ulong)ReadU32(buf, off) | ((ulong)ReadU32(buf, off + 4) << 32);
        }

        public static ushort ReadU16BE(byte[] buf, long off)
        {
            return (ushort)((buf[off] << 8) | buf[off + 1]);
        }

        public static uint ReadU32BE(byte[] buf, long off)
        {
            return (uint)((buf[off] << 24) | (buf[off + 1] << 16) | (buf[off + 2] << 8) | buf[off + 3]);
        }

        public static ulong ReadU64BE(byte[] buf, long off)
        {
            return ((ulong)ReadU32BE(buf, off) << 32) | ReadU32BE(buf, off + 4);
        }

        /// <summary>Magic sniff: "hsqs" is little-endian, "sqsh" big-endian.</summary>
        public static bool HasMagic(byte[] sb, out bool bigEndian)
        {
            bigEndian = false;
            if (sb.Length < 4)
            {
                return false;
            }
            uint raw = ReadU32(sb, 0);
            if (raw == Magic)
            {
                return true;
            }
            if (raw == SwappedMagic)
            {
                bigEndian = true;
                return true;
            }
            return false;
        }

        public static byte[] ReadBytes(Stream stream, long offset, int count)
        {
            if (offset < 0 || offset + count > stream.Length)
            {
                throw new IOException("squashfs read out of range");
            }
            long saved = stream.Position;
            try
            {
                stream.Position = offset;
                var buf = new byte[count];
                int done = 0;
                while (done < count)
                {
                    int n = stream.Read(buf, done, count - done);
                    if (n <= 0)
                    {
                        throw new IOException("squashfs short read");
                    }
                    done += n;
                }
                return buf;
            }
            finally
            {
                stream.Position = saved;
            }
        }
    }
}
