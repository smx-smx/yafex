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

using Yafex.FileSystems.Base;
using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.Ubifs
{
    /// <summary>
    /// Read-only UBIFS driver (e.g. Broadcom/Askey rootfs volumes): opens a
    /// flat volume payload (LEBs concatenated, LEB size from the opening
    /// superblock) and exposes it through DiscFileSystem.
    /// </summary>
    public sealed class UbifsFileSystem
        : VfsReadOnlyFileSystem<UbifsDirectoryEntry, UbifsFile, UbifsDirectory, UbifsContext>
    {
        public UbifsFileSystem(Stream stream)
            : base(new DiscFileSystemOptions())
        {
            var context = UbifsContext.Open(stream);
            Context = context;
            UbifsInode root = context.GetInode(1);
            RootDirectory = new UbifsDirectory(context, root);
        }

        public static bool Detect(Stream stream)
        {
            if (stream == null || !stream.CanSeek || stream.Length < 64)
            {
                return false;
            }
            try
            {
                long pos = stream.Position;
                try
                {
                    var prefix = new byte[24];
                    stream.Position = 0;
                    int got = 0;
                    while (got < prefix.Length)
                    {
                        int n = stream.Read(prefix, got, prefix.Length - got);
                        if (n <= 0) return false;
                        got += n;
                    }
                    if (UbifsConstants.ReadU32(prefix, 0) != UbifsConstants.Magic) return false;
                    if (UbifsConstants.ReadU32(prefix, 20) != UbifsConstants.TypeSb) return false;
                    uint len = UbifsConstants.ReadU32(prefix, 16);
                    if (len < 32 || len > 1 << 20 || len > stream.Length) return false;
                    var node = new byte[len];
                    stream.Position = 0;
                    got = 0;
                    while (got < node.Length)
                    {
                        int n = stream.Read(node, got, node.Length - got);
                        if (n <= 0) return false;
                        got += n;
                    }
                    return UbifsConstants.CrcRaw(node, 8, (int)len - 8)
                        == UbifsConstants.ReadU32(node, 4);
                }
                finally
                {
                    stream.Position = pos;
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public override string FriendlyName
        {
            get { return "UBIFS"; }
        }

        public override string VolumeLabel
        {
            get { return string.Empty; }
        }

        public override long Size
        {
            get { throw new NotSupportedException("Filesystem size is not (yet) supported"); }
        }

        public override long UsedSpace
        {
            get { throw new NotSupportedException("Filesystem size is not (yet) supported"); }
        }

        public override long AvailableSpace
        {
            get { throw new NotSupportedException("Filesystem size is not (yet) supported"); }
        }

        protected override UbifsFile ConvertDirEntryToFile(UbifsDirectoryEntry dirEntry)
        {
            UbifsInode inode = Context.GetInode(dirEntry.Ino);
            if (dirEntry.IsDirectory)
            {
                return new UbifsDirectory(Context, inode);
            }
            if (dirEntry.IsSymlink)
            {
                return new UbifsSymlink(Context, inode);
            }
            return new UbifsFile(Context, inode);
        }
    }
}
