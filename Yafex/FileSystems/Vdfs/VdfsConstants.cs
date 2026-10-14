using System;
using System.IO;

namespace Yafex.FileSystems.Vdfs
{
    /// <summary>
    /// On-disk layout constants for Samsung VDFS (Vertically Deliberate File
    /// System) v4, layout "2007". Values describe the read-only images emitted
    /// by mkfs.vdfs (e.g. Tizen TV firmware partitions).
    /// </summary>
    internal static class VdfsConstants
    {
        public const int SectorSize = 512;

        // Volume header page (sectors 0..7, two copies)
        public const long VbOffset = 0;
        public const long SbOffset = SectorSize * 2;
        public const long ExsbOffset = SectorSize * 3;
        public const int ExsbSectors = 5;

        public const string VbSignature = "VDFS";
        public const string Layout2007 = "2007";
        public const string Layout2006 = "2006";
        public const string BtreeHeadMagic = "eHND";
        public const string BaseTableMagic = "CoWB";

        // Superblock field offsets (sector 2)
        public const int SbMaxBlocksOff = 8;
        public const int SbVolumeNameOff = 0x2C;
        public const int SbVolumeNameLen = 16;
        public const int SbLogBlockSizeOff = 0xA4;
        public const int SbLogSuperPageOff = 0xA5;
        public const int SbCaseInsensitiveOff = 0xA7;
        public const int SbReadOnlyOff = 0xA8;
        public const int SbCrcPresentOff = 0xA9;
        public const int SbHashTypeOff = 0xAB;
        public const int SbSignTypeOff = 0xAD;
        public const int SbInodeCountOff = 0xF0;

        // Extended superblock field offsets (sectors 3..7)
        public const int ExsbFilesCountOff = 0;
        public const int ExsbVolumeBodyOff = 0x10;
        public const int ExsbMetaTbcOff = 0x40;
        public const int ExsbTablesOff = 0x48;
        public const int ExsbMetaOff = 0x58;
        public const int MetaBtreeExtents = 96;
        public const int ExtentSize = 16;
        public const int ExsbVolumeBlocksOff = 0x668;

        // Snapshot base table (at the tables extent)
        public const int SfCount = 5;
        public const int BaseTableLastPageOff = 24;
        public const int BaseTableOffsetsOff = 24 + SfCount * 8;
        public const int BaseTableRecordSize = 16;

        // Special inodes (current on-disk generation)
        public const ulong RootDirObjId = 0;
        public const ulong RootIno = 1;
        public const ulong CatTreeIno = 3;
        public const ulong CatTreeInoLegacy = 2;
        public const ulong ExtTreeIno = 5;
        public const ulong ExtTreeInoLegacy = 4;
        public const ulong FirstFileIno = 9;

        // B-tree node descriptor
        public const int NodeDescrSize = 32;
        public const int NodeFreeOff = 12;
        public const int NodeRecsOff = 14;
        public const int NodeIdOff = 16;
        public const int NodePrevOff = 20;
        public const int NodeNextOff = 24;
        public const int NodeTypeOff = 28;
        public const uint NodeIndex = 1;
        public const uint NodeLeaf = 2;
        public const int NodeTailReserved = 4;

        // Catalog key (magic-agnostic: compared by parent/name/object only)
        public const int KeyMagicLen = 4;
        public const int KeyLenOff = 4;
        public const int RecordLenOff = 6;
        public const int KeyParentOff = 8;
        public const int KeyObjectOff = 16;
        public const int KeyTypeOff = 24;
        public const int KeyNameLenOff = 25;
        public const int KeyNameOff = 26;
        public const int MaxNameLen = 255;

        public const byte RecFolder = 0x01;
        public const byte RecFile = 0x02;
        public const byte RecHLink = 0x03;
        public const byte RecILink = 0x05;

        // Catalog folder record (80 bytes)
        public const int FolderRecordSize = 80;
        public const int FolderFlagsOff = 0;
        public const int FolderModeOff = 40;
        public const int FolderUidOff = 44;
        public const int FolderGidOff = 48;

        // Data fork (232 bytes at folder-record end for files)
        public const int ForkSizeOff = 0;
        public const int ForkBlocksOff = 8;
        public const int ForkDataOff = 16;
        public const int ForkDataLen = 216;
        public const int ForkSize = 232;
        public const int FileRecordSize = FolderRecordSize + ForkSize;
        public const int ExtentsInFork = 9;
        public const int IExtentSize = 24;

        public const uint FlagCompressed = 1u << 13;
        public const uint FlagInlineData = 1u << 19;
        public const uint FlagCompInline = 1u << 20;

        // Compressed file descriptor (at the blob tail)
        public const int CompDescrLen06 = 40;
        public const int CompDescrLen05 = 32;
        public const ushort CompLayout05 = 0x0005;
        public const ushort CompLayout06 = 0x0006;
        public const ushort ChunkFlagRaw = 0x1;
        public const int CompExtentSize = 16;

        public const ushort ModeTypeMask = 0xF000;
        public const ushort ModeSymlink = 0xA000;
        public const ushort ModeDir = 0x4000;

        public static ulong ReadU64(byte[] buf, int off)
        {
            return BitConverter.ToUInt64(buf, off);
        }

        public static uint ReadU32(byte[] buf, int off)
        {
            return BitConverter.ToUInt32(buf, off);
        }

        public static ushort ReadU16(byte[] buf, int off)
        {
            return BitConverter.ToUInt16(buf, off);
        }

        public static void ReadExact(Stream s, byte[] buf, int off, int count)
        {
            while (count > 0)
            {
                int n = s.Read(buf, off, count);
                if (n <= 0)
                {
                    throw new EndOfStreamException("truncated VDFS image");
                }
                off += n;
                count -= n;
            }
        }

        public static byte[] ReadBytes(Stream s, long pos, int count)
        {
            s.Position = pos;
            byte[] buf = new byte[count];
            ReadExact(s, buf, 0, count);
            return buf;
        }
    }
}
