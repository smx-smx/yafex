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

using System.IO;
using Yafex.FileSystems.Base;
using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.SquashFs
{
    internal class Context : VfsContext
    {
        public MetablockReader DirectoryReader { get; set; }

        public MetablockReader[] FragmentTableReaders { get; set; }

        public MetablockReader InodeReader { get; set; }

        public Stream RawStream { get; set; }

        public ReadBlock ReadBlock { get; set; }

        public ReadMetaBlock ReadMetaBlock { get; set; }
        public SuperBlock SuperBlock { get; set; }

        public MetablockReader[] UidGidTableReaders { get; set; }

        /// <summary>
        /// Parsed directory listings by inode number. Directory objects are
        /// recreated on every path lookup (the VFS file cache only keeps 20
        /// strong references), so listings are cached here, for the lifetime
        /// of the (read-only) filesystem. Not keyed by listing position: empty
        /// directories carry an arbitrary position, often another directory's.
        /// </summary>
        public System.Collections.Generic.Dictionary<uint, Directory.Listing> Listings { get; }
            = new System.Collections.Generic.Dictionary<uint, Directory.Listing>();
    }
}