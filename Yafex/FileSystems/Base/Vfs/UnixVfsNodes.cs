#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
using System;
using System.IO;

using Yafex.FileSystems.Base.Streams;
using Yafex.Support;

namespace Yafex.FileSystems.Base.Vfs
{
    /// <summary>
    /// Directory entry for the read-only Unix-style drivers (jffs2, ubifs, ufs,
    /// squashfs 2/3): attributes follow the entry type and every timestamp is
    /// the inode mtime. Drivers supply the name, type checks and mtime.
    /// </summary>
    public abstract class UnixVfsDirEntry : VfsDirEntry
    {
        /// <summary>Inode modification time, seconds since the Unix epoch.</summary>
        protected abstract long Mtime { get; }

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

        public override bool HasVfsFileAttributes => false;

        public override bool HasVfsTimeInfo => false;

        public override DateTime CreationTimeUtc => UnixTime.ToDateTime(Mtime);

        public override DateTime LastAccessTimeUtc => UnixTime.ToDateTime(Mtime);

        public override DateTime LastWriteTimeUtc => UnixTime.ToDateTime(Mtime);
    }

    /// <summary>
    /// Read-only file for the Unix-style drivers: mtime-only timestamps and
    /// content opened once on first access via <see cref="OpenContent"/>.
    /// </summary>
    public abstract class UnixVfsFile : IVfsFile
    {
        private IBuffer _content;

        /// <summary>Inode modification time, seconds since the Unix epoch.</summary>
        protected abstract long Mtime { get; }

        public abstract long FileLength { get; }

        protected abstract IBuffer OpenContent();

        public IBuffer FileContent => _content ??= OpenContent();

        public DateTime CreationTimeUtc
        {
            get => UnixTime.ToDateTime(Mtime);
            set => throw new NotSupportedException();
        }

        public DateTime LastAccessTimeUtc
        {
            get => UnixTime.ToDateTime(Mtime);
            set => throw new NotSupportedException();
        }

        public DateTime LastWriteTimeUtc
        {
            get => UnixTime.ToDateTime(Mtime);
            set => throw new NotSupportedException();
        }

        public FileAttributes FileAttributes
        {
            get => FileAttributes.ReadOnly | FileAttributes.Normal;
            set => throw new NotSupportedException();
        }
    }
}
