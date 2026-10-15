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
using System.Collections.Generic;
using Yafex.FileSystems.Base;
using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.SquashFs
{
    internal class Directory : File, IVfsDirectory<DirectoryEntry, File>
    {
        private readonly IDirectoryInode _dirInode;

        public Directory(Context context, Inode inode, MetadataRef inodeRef)
            : base(context, inode, inodeRef)
        {
            _dirInode = inode as IDirectoryInode;
            if (_dirInode == null)
            {
                throw new ArgumentException("Inode is not a directory", nameof(inode));
            }
        }

        /// <summary>A directory's entries, in on-disk order and by name.</summary>
        internal sealed class Listing
        {
            public List<DirectoryEntry> Entries;
            public Dictionary<string, DirectoryEntry> ByName;
        }

        public ICollection<DirectoryEntry> AllEntries => GetListing().Entries;

        /// <summary>
        /// The listing, parsed once per filesystem (see <see cref="Context.Listings"/>):
        /// path lookups otherwise re-read every directory on the way each time.
        /// </summary>
        private Listing GetListing()
        {
            // the directory reader and the cache are shared by the whole filesystem
            lock (Context)
            {
                var key = Inode.InodeNumber;
                if (Context.Listings.TryGetValue(key, out var cached)) return cached;

                List<DirectoryEntry> records = new List<DirectoryEntry>();

                MetablockReader reader = Context.DirectoryReader;
                reader.SetPosition(_dirInode.StartBlock, _dirInode.Offset);

                // For some reason, always 3 greater than actual..
                // NOTE: MetablockReader.DistanceFrom cannot be used here: it
                // scales metablock address deltas (compressed file offsets)
                // by the decompressed metablock size, so it overshoots as
                // soon as a listing spans metablocks. Count consumed bytes
                // explicitly instead (header 12, record 8 + name length).
                long consumed = 0;
                while (consumed < _dirInode.FileSize - 3)
                {
                    DirectoryHeader header = DirectoryHeader.ReadFrom(reader);
                    consumed += 12;

                    for (int i = 0; i < header.Count + 1; ++i)
                    {
                        DirectoryRecord record = DirectoryRecord.ReadFrom(reader);
                        consumed += 8 + record.Name.Length;
                        records.Add(new DirectoryEntry(header, record));
                    }
                }

                var byName = new Dictionary<string, DirectoryEntry>(records.Count, StringComparer.Ordinal);
                foreach (var entry in records)
                {
                    byName.TryAdd(entry.FileName, entry);
                }
                var listing = new Listing { Entries = records, ByName = byName };
                Context.Listings[key] = listing;
                return listing;
            }
        }

        public DirectoryEntry Self
        {
            get { return null; }
        }

        public DirectoryEntry GetEntryByName(string name)
        {
            return GetListing().ByName.TryGetValue(name, out var entry) ? entry : null;
        }

        public DirectoryEntry CreateNewFile(string name)
        {
            throw new NotSupportedException();
        }
    }
}