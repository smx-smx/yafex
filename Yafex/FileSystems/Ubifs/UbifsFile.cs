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

using Yafex.Support;
namespace Yafex.FileSystems.Ubifs
{
    internal sealed class UbifsMemoryBuffer : IBuffer
    {
        private readonly byte[] _data;

        public UbifsMemoryBuffer(byte[] data)
        {
            _data = data;
        }

        public bool CanRead
        {
            get { return true; }
        }

        public bool CanWrite
        {
            get { return false; }
        }

        public long Capacity
        {
            get { return _data.Length; }
        }

        public IEnumerable<StreamExtent> Extents
        {
            get { yield return new StreamExtent(0, _data.Length); }
        }

        public int Read(long pos, byte[] buffer, int offset, int count)
        {
            if (pos >= _data.Length)
            {
                return 0;
            }
            if (pos + count > _data.Length)
            {
                count = (int)(_data.Length - pos);
            }
            Array.Copy(_data, pos, buffer, offset, count);
            return count;
        }

        public void Write(long pos, byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public void Clear(long pos, int count)
        {
            throw new NotSupportedException();
        }

        public void Flush()
        {
        }

        public void SetCapacity(long value)
        {
            throw new NotSupportedException();
        }

        public IEnumerable<StreamExtent> GetExtentsInRange(long start, long count)
        {
            yield return new StreamExtent(0, _data.Length);
        }
    }

    public class UbifsFile : IVfsFile
    {
        protected readonly UbifsContext Context;
        protected readonly UbifsInode Inode;
        private IBuffer _content;

        public UbifsFile(UbifsContext context, UbifsInode inode)
        {
            Context = context;
            Inode = inode;
        }

        public virtual DateTime CreationTimeUtc
        {
            get { return UnixTime.ToDateTime(Inode.Mtime); }
            set { throw new NotSupportedException(); }
        }

        public virtual DateTime LastAccessTimeUtc
        {
            get { return UnixTime.ToDateTime(Inode.Mtime); }
            set { throw new NotSupportedException(); }
        }

        public virtual DateTime LastWriteTimeUtc
        {
            get { return UnixTime.ToDateTime(Inode.Mtime); }
            set { throw new NotSupportedException(); }
        }


        public virtual FileAttributes FileAttributes
        {
            get { return FileAttributes.ReadOnly | FileAttributes.Normal; }
            set { throw new NotSupportedException(); }
        }

        public virtual long FileLength
        {
            get { return (long)Math.Min(Inode.Size, long.MaxValue); }
        }

        public virtual IBuffer FileContent
        {
            get
            {
                if (_content == null)
                {
                    _content = OpenContent();
                }
                return _content;
            }
        }

        protected virtual IBuffer OpenContent()
        {
            return new UbifsMemoryBuffer(Context.ReadFile(Inode.Ino));
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
            return new UbifsMemoryBuffer(new byte[0]);
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
            return new UbifsMemoryBuffer(new byte[0]);
        }
    }
}
