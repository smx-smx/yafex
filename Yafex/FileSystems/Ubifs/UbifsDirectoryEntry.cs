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

using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.Ubifs
{
    public sealed class UbifsDirectoryEntry : UnixVfsDirEntry
    {
        private readonly UbifsContext _context;
        private readonly UbifsDirent _dirent;
        private UbifsInode _inode;

        public UbifsDirectoryEntry(UbifsContext context, UbifsDirent dirent)
        {
            _context = context;
            _dirent = dirent;
        }

        public uint Ino
        {
            get { return _dirent.Ino; }
        }

        private UbifsInode Inode
        {
            get
            {
                if (_inode == null)
                {
                    _inode = _context.GetInode(_dirent.Ino);
                }
                return _inode;
            }
        }

        protected override long Mtime => Inode.Mtime;

        public override string FileName
        {
            get { return _dirent.Name; }
        }

        public override bool IsDirectory
        {
            get { return (Inode.Mode & 0xF000) == 0x4000; }
        }

        public override bool IsSymlink
        {
            get { return (Inode.Mode & 0xF000) == 0xA000; }
        }

        public override long UniqueCacheId
        {
            get { return _dirent.Ino; }
        }
    }
}
