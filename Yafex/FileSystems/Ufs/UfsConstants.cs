using System;
using System.IO;

namespace Yafex.FileSystems.Ufs
{
    // On-disk layout reference: FreeBSD sys/ufs/ffs/fs.h (struct fs),
    // sys/ufs/ufs/dinode.h (ufs1/ufs2_dinode), sys/ufs/ufs/dir.h (struct direct).
    // Implemented from those numeric layouts.
    internal static class UfsConstants
    {
        public const uint Ufs1Magic = 0x00019554;
        public const uint Ufs2Magic = 0x19540119;

        // fs_magic sits at the end of struct fs (sizeof(struct fs) == 1376).
        public const int MagicOffset = 1372;
        public const int SuperblockLength = 1376;

        // Superblock search locations (FreeBSD SBLOCKSEARCH): UFS2 at 64K,
        // UFS1 at 8K, floppy at 0, piggy at 256K.
        public const long SuperblockUfs1 = 8192;
        public const long SuperblockUfs2 = 65536;
        public const long SuperblockFloppy = 0;
        public const long SuperblockPiggy = 262144;

        public const uint CgMagic = 0x090255;
        public const uint RootIno = 2;

        public const int DirectCount = 12;
        public const int IndirectCount = 3;

        public const int ModeTypeMask = 0xF000;
        public const int ModeDirectory = 0x4000;
        public const int ModeFile = 0x8000;
        public const int ModeSymlink = 0xA000;

        public const byte DtypeDir = 4;
        public const byte DtypeFile = 8;
        public const byte DtypeSymlink = 10;

        public const int DirEntryHeader = 8;
        public const int MaxNameLength = 255;

        public static ushort ReadU16(byte[] buf, int off, bool bigEndian)
        {
            if (bigEndian)
            {
                return (ushort)((buf[off] << 8) | buf[off + 1]);
            }
            return (ushort)(buf[off] | (buf[off + 1] << 8));
        }

        public static uint ReadU32(byte[] buf, int off, bool bigEndian)
        {
            if (bigEndian)
            {
                return (uint)((buf[off] << 24) | (buf[off + 1] << 16) | (buf[off + 2] << 8) | buf[off + 3]);
            }
            return (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));
        }

        public static ulong ReadU64(byte[] buf, int off, bool bigEndian)
        {
            uint lo, hi;
            if (bigEndian)
            {
                hi = ReadU32(buf, off, true);
                lo = ReadU32(buf, off + 4, true);
            }
            else
            {
                lo = ReadU32(buf, off, false);
                hi = ReadU32(buf, off + 4, false);
            }
            return ((ulong)hi << 32) | lo;
        }

        public static byte[] ReadBytes(Stream stream, long position, int count)
        {
            if (position < 0 || count < 0 || position + count > stream.Length)
            {
                throw new IOException("ufs read out of range");
            }
            var buf = new byte[count];
            stream.Position = position;
            int done = 0;
            while (done < count)
            {
                int n = stream.Read(buf, done, count - done);
                if (n <= 0)
                {
                    throw new IOException("ufs short read");
                }
                done += n;
            }
            return buf;
        }
    }
}
