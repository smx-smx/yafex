//
// Copyright (c) 2008-2011, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System;
using System.IO;
using DiscUtils.Streams;

namespace Yafex.FileSystems.SquashFs
{
    /// <summary>
    /// Extended regular-file inode (64-bit block/file sizes, link count and
    /// xattr reference). Layout per squashfs_fs.h squashfs_lreg_inode.
    /// </summary>
    internal class ExtendedFileInode : RegularInode
    {
        private ulong _fileSize;
        private ulong _sparse;
        private uint _xattr;

        public override long FileSize
        {
            get { return (long)_fileSize; }

            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Negative file size");
                }

                _fileSize = (ulong)value;
            }
        }

        public override int Size
        {
            get { return 60; }
        }

        public override int ReadFrom(byte[] buffer, int offset)
        {
            base.ReadFrom(buffer, offset);

            // NOTE: replaces the 32-bit fields parsed by RegularInode.
            StartBlock = EndianUtilities.ToUInt32LittleEndian(buffer, offset + 16);
            ulong startBlockHigh = EndianUtilities.ToUInt32LittleEndian(buffer, offset + 20);
            _fileSize = EndianUtilities.ToUInt64LittleEndian(buffer, offset + 24);
            _sparse = EndianUtilities.ToUInt64LittleEndian(buffer, offset + 32);
            NumLinks = EndianUtilities.ToInt32LittleEndian(buffer, offset + 40);
            FragmentKey = EndianUtilities.ToUInt32LittleEndian(buffer, offset + 44);
            FragmentOffset = EndianUtilities.ToUInt32LittleEndian(buffer, offset + 48);
            _xattr = EndianUtilities.ToUInt32LittleEndian(buffer, offset + 52);

            if (startBlockHigh != 0)
            {
                throw new IOException("SquashFS start block exceeds 32 bits: " + startBlockHigh);
            }

            return 60;
        }
    }
}
