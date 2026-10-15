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
using System.Collections.Generic;
using System.IO;
using System.Text;

using Yafex.FileSystems.Base.Streams;
using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.Ubifs
{

    public class UbifsFile : UnixVfsFile
    {
        protected readonly UbifsContext Context;
        protected readonly UbifsInode Inode;

        public UbifsFile(UbifsContext context, UbifsInode inode)
        {
            Context = context;
            Inode = inode;
        }

        protected override long Mtime => Inode.Mtime;

        public override long FileLength
        {
            get { return (long)Math.Min(Inode.Size, long.MaxValue); }
        }

        protected override IBuffer OpenContent()
        {
            return new ReadOnlyByteBuffer(Context.ReadFile(Inode.Ino));
        }
    }

    public sealed class UbifsSymlink : UbifsFile, IVfsSymlink<UbifsDirectoryEntry, UbifsFile>
    {
        public UbifsSymlink(UbifsContext context, UbifsInode inode)
            : base(context, inode)
        {
        }

        public string TargetPath
        {
            get
            {
                byte[] raw = Context.ReadSymlink(Inode.Ino);
                int len = raw.Length;
                while (len > 0 && raw[len - 1] == 0)
                {
                    len--;
                }
                return Encoding.UTF8.GetString(raw, 0, len);
            }
        }

        public override long FileLength
        {
            get { return 0; }
        }

        protected override IBuffer OpenContent()
        {
            return ReadOnlyByteBuffer.Empty;
        }
    }

    public sealed class UbifsDirectory
        : UbifsFile, IVfsDirectory<UbifsDirectoryEntry, UbifsFile>
    {
        private readonly uint _ino;

        public UbifsDirectory(UbifsContext context, UbifsInode inode)
            : base(context, inode)
        {
            _ino = inode.Ino;
        }

        public ICollection<UbifsDirectoryEntry> AllEntries
        {
            get
            {
                var result = new List<UbifsDirectoryEntry>();
                foreach (UbifsDirent e in Context.ReadDirectory(_ino))
                {
                    result.Add(new UbifsDirectoryEntry(Context, e));
                }
                return result;
            }
        }

        public UbifsDirectoryEntry Self
        {
            get { return null; }
        }

        public UbifsDirectoryEntry GetEntryByName(string name)
        {
            foreach (UbifsDirectoryEntry e in AllEntries)
            {
                if (e.FileName == name)
                {
                    return e;
                }
            }
            return null;
        }

        public UbifsDirectoryEntry CreateNewFile(string name)
        {
            throw new NotSupportedException();
        }

        public override long FileLength
        {
            get { return 0; }
        }

        protected override IBuffer OpenContent()
        {
            return ReadOnlyByteBuffer.Empty;
        }
    }
}
