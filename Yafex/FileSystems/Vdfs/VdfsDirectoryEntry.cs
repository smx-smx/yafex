using System;
using System.Collections.Generic;
using System.IO;

using DiscUtils.Streams;
using DiscUtils;
using DiscUtils.Vfs;

namespace Yafex.FileSystems.Vdfs
{
    public sealed class VdfsDirectoryEntry : VfsDirEntry
    {
        private readonly VdfsCatRecord _rec;
        private readonly uint _flags;
        private readonly ushort _mode;

        public VdfsDirectoryEntry(VdfsCatRecord rec)
        {
            _rec = rec;
            if (rec.Value.Length >= VdfsConstants.FolderRecordSize)
            {
                _flags = VdfsConstants.ReadU32(rec.Value, VdfsConstants.FolderFlagsOff);
                _mode = VdfsConstants.ReadU16(rec.Value, VdfsConstants.FolderModeOff);
            }
        }

        public VdfsCatRecord Record
        {
            get { return _rec; }
        }

        public uint Flags
        {
            get { return _flags; }
        }

        public ushort Mode
        {
            get { return _mode; }
        }

        public bool IsCompressed
        {
            get { return (_flags & VdfsConstants.FlagCompressed) != 0; }
        }

        public override string FileName
        {
            get { return _rec.Name; }
        }

        public override bool IsDirectory
        {
            get { return _rec.Type == VdfsConstants.RecFolder; }
        }

        public override bool IsSymlink
        {
            get
            {
                return _rec.Type == VdfsConstants.RecFile
                    && (_mode & VdfsConstants.ModeTypeMask) == VdfsConstants.ModeSymlink;
            }
        }

        public override FileAttributes FileAttributes
        {
            get
            {
                FileAttributes attr = IsDirectory ? FileAttributes.Directory : FileAttributes.Normal;
                if (IsSymlink)
                {
                    attr |= FileAttributes.ReparsePoint;
                }
                return attr;
            }
        }

        public override bool HasVfsFileAttributes
        {
            get { return false; }
        }

        public override bool HasVfsTimeInfo
        {
            get { return false; }
        }

        private static DateTime ReadTime(byte[] value, int off)
        {
            if (value.Length < off + 12)
            {
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }
            ulong secs = VdfsConstants.ReadU32(value, off)
                | ((ulong)VdfsConstants.ReadU32(value, off + 4) << 32);
            // Precision past 2^53 seconds is irrelevant for firmware images.
            double s = secs > (ulong)long.MaxValue ? 0 : (double)secs;
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(s);
        }

        public override DateTime CreationTimeUtc
        {
            get { return ReadTime(_rec.Value, 44); }
        }

        public override DateTime LastAccessTimeUtc
        {
            get { return ReadTime(_rec.Value, 68); }
        }

        public override DateTime LastWriteTimeUtc
        {
            get { return ReadTime(_rec.Value, 56); }
        }

        public override long UniqueCacheId
        {
            get { return (long)_rec.ObjectId; }
        }
    }
}
