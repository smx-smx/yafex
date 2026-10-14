using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.Ufs
{
    // Read-only UFS (FFS) engine: UFS1 and UFS2, either endianness.
    // Block pointers are fragment numbers; the final block of a file may
    // occupy a frag-sized tail (blksize formula from fs.h).
    public sealed class UfsContext : VfsContext
    {
        private readonly Stream _stream;
        private readonly UfsSuperblock _sb;
        private readonly Dictionary<uint, UfsInode> _inodes = new Dictionary<uint, UfsInode>();

        private UfsContext(Stream stream, UfsSuperblock sb)
        {
            _stream = stream;
            _sb = sb;
        }

        public UfsSuperblock Superblock
        {
            get { return _sb; }
        }

        public static UfsContext Open(Stream stream)
        {
            if (stream == null || !stream.CanSeek)
            {
                throw new IOException("ufs needs a seekable stream");
            }
            long[] bases = new long[]
            {
                UfsConstants.SuperblockUfs2,
                UfsConstants.SuperblockUfs1,
                UfsConstants.SuperblockFloppy,
                UfsConstants.SuperblockPiggy,
            };
            foreach (long sbBase in bases)
            {
                foreach (bool bigEndian in new bool[] { false, true })
                {
                    byte[] raw = TryReadSuperblock(stream, sbBase);
                    if (raw == null)
                    {
                        continue;
                    }
                    uint magic = UfsConstants.ReadU32(raw, UfsConstants.MagicOffset, bigEndian);
                    if (magic != UfsConstants.Ufs1Magic && magic != UfsConstants.Ufs2Magic)
                    {
                        continue;
                    }
                    // claimed geometry first (strict field checks), then
                    // halved fallbacks for stale template superblocks
                    UfsContext proved = TryGeometries(stream, raw, bigEndian);
                    if (proved != null)
                    {
                        return proved;
                    }
                }
            }
            throw new IOException("no ufs filesystem found");
        }

        private static UfsContext TryGeometries(Stream stream, byte[] raw, bool bigEndian)
        {
            int claimedB = (int)UfsConstants.ReadU32(raw, 48, bigEndian);
            int claimedF = (int)UfsConstants.ReadU32(raw, 52, bigEndian);
            UfsContext proved = TryOpenWith(stream, raw, bigEndian, claimedB, claimedF, true);
            if (proved != null)
            {
                return proved;
            }
            if (claimedF < 512 || claimedF > (1 << 20) || claimedB < claimedF || claimedB > (1 << 24))
            {
                return null;
            }
            for (long fragSize = claimedF / 2; fragSize >= 512; fragSize /= 2)
            {
                for (long blockSize = fragSize; blockSize <= fragSize * 8 && blockSize <= 65536; blockSize *= 2)
                {
                    if (blockSize < 1024)
                    {
                        continue;
                    }
                    if (blockSize == claimedB && fragSize == claimedF)
                    {
                        continue;
                    }
                    proved = TryOpenWith(stream, raw, bigEndian, (int)blockSize, (int)fragSize, false);
                    if (proved != null)
                    {
                        return proved;
                    }
                }
            }
            return null;
        }

        private static UfsContext TryOpenWith(Stream stream, byte[] raw, bool bigEndian, int blockSize, int fragSize, bool strict)
        {
            UfsSuperblock sb;
            try
            {
                sb = strict
                    ? UfsSuperblock.Read(raw, bigEndian)
                    : UfsSuperblock.ReadWithGeometry(raw, bigEndian, blockSize, fragSize);
            }
            catch (IOException)
            {
                return null;
            }
            var ctx = new UfsContext(stream, sb);
            try
            {
                UfsInode root = ctx.GetInode(UfsConstants.RootIno);
                if (!root.IsDirectory)
                {
                    return null;
                }
                // '.' must loop back to the root: proves dir parsing works
                foreach (UfsDirent e in ctx.ReadDirectory(UfsConstants.RootIno))
                {
                    if (e.Name == "." && e.Ino == UfsConstants.RootIno)
                    {
                        return ctx;
                    }
                }
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static byte[] TryReadSuperblock(Stream stream, long sbBase)
        {
            try
            {
                if (sbBase + UfsConstants.SuperblockLength > stream.Length)
                {
                    return null;
                }
                return UfsConstants.ReadBytes(stream, sbBase, UfsConstants.SuperblockLength);
            }
            catch (IOException)
            {
                return null;
            }
        }

        public UfsInode GetInode(uint ino)
        {
            UfsInode inode;
            if (_inodes.TryGetValue(ino, out inode))
            {
                return inode;
            }
            if (ino == 0 || ino / _sb.InodesPerGroup >= _sb.NumCylGroups)
            {
                throw new IOException("ufs inode out of range: " + ino);
            }
            ulong frag = _sb.InodeFrag(ino);
            long pos = _sb.FragBytes(frag) + (ino % _sb.InodesPerBlock) * _sb.DinodeSize;
            byte[] raw = UfsConstants.ReadBytes(_stream, pos, _sb.DinodeSize);
            inode = UfsInode.Read(raw, ino, _sb.IsUfs2, _sb.BigEndian);
            _inodes[ino] = inode;
            return inode;
        }

        private long FragRoundup(long size)
        {
            return ((size + _sb.FragSize - 1) / _sb.FragSize) * _sb.FragSize;
        }

        // Usable bytes of logical block lbn for a file of size.
        private long BlockRun(long size, long lbn)
        {
            if (lbn >= UfsConstants.DirectCount || size >= (lbn + 1) * _sb.BlockSize)
            {
                return _sb.BlockSize;
            }
            return FragRoundup(size - lbn * _sb.BlockSize);
        }

        private ulong[] ReadPointers(ulong frag)
        {
            if (frag == 0)
            {
                return null;
            }
            byte[] block = UfsConstants.ReadBytes(_stream, _sb.FragBytes(frag), _sb.BlockSize);
            ulong[] addrs = new ulong[_sb.AddrsPerBlock];
            for (uint i = 0; i < addrs.Length; i++)
            {
                if (_sb.IsUfs2)
                {
                    addrs[i] = UfsConstants.ReadU64(block, (int)(i * 8), _sb.BigEndian);
                }
                else
                {
                    addrs[i] = UfsConstants.ReadU32(block, (int)(i * 4), _sb.BigEndian);
                }
            }
            return addrs;
        }

        // Fragment holding file offset, plus how many bytes are usable there.
        // A zero fragment anywhere in the chain means a hole (zeros).
        private void MapBlock(UfsInode inode, long lbn, long size, out ulong frag, out long run)
        {
            run = BlockRun(size, lbn);
            frag = 0;
            if (lbn < UfsConstants.DirectCount)
            {
                frag = inode.Direct[lbn];
                return;
            }
            ulong n = _sb.AddrsPerBlock;
            long rel = lbn - UfsConstants.DirectCount;
            if (rel < (long)n)
            {
                ulong[] single = ReadPointers(inode.Indirect[0]);
                if (single != null)
                {
                    frag = single[rel];
                }
                return;
            }
            rel -= (long)n;
            if (rel < (long)n * (long)n)
            {
                ulong[] dbl = ReadPointers(inode.Indirect[1]);
                if (dbl == null)
                {
                    return;
                }
                ulong[] single = ReadPointers(dbl[rel / (long)n]);
                if (single != null)
                {
                    frag = single[rel % (long)n];
                }
                return;
            }
            rel -= (long)n * (long)n;
            ulong perDbl = (ulong)n * (ulong)n;
            if ((ulong)rel / perDbl >= n)
            {
                throw new IOException("ufs block out of range");
            }
            ulong[] tpl = ReadPointers(inode.Indirect[2]);
            if (tpl == null)
            {
                return;
            }
            ulong[] dbl2 = ReadPointers(tpl[(ulong)rel / perDbl]);
            if (dbl2 == null)
            {
                return;
            }
            ulong[] single2 = ReadPointers(dbl2[((ulong)rel % perDbl) / n]);
            if (single2 != null)
            {
                frag = single2[((ulong)rel % perDbl) % n];
            }
        }

        public byte[] ReadFile(uint ino)
        {
            UfsInode inode = GetInode(ino);
            if (inode.Size == 0)
            {
                return new byte[0];
            }
            if (inode.Size > 0x7FFFFFFF || inode.Size > (ulong)_stream.Length)
            {
                throw new IOException("ufs file too large");
            }
            var output = new byte[(int)inode.Size];
            long pos = 0;
            long lbn = 0;
            while (pos < (long)inode.Size)
            {
                ulong frag;
                long run;
                MapBlock(inode, lbn, (long)inode.Size, out frag, out run);
                long want = Math.Min(run, (long)inode.Size - pos);
                if (frag != 0)
                {
                    byte[] chunk = UfsConstants.ReadBytes(_stream, _sb.FragBytes(frag), (int)want);
                    Array.Copy(chunk, 0, output, (int)pos, (int)want);
                }
                // holes stay zero-filled
                pos += want;
                lbn++;
            }
            return output;
        }

        public List<UfsDirent> ReadDirectory(uint ino)
        {
            UfsInode inode = GetInode(ino);
            if (!inode.IsDirectory)
            {
                throw new IOException("ufs not a directory: " + ino);
            }
            byte[] data = ReadFile(ino);
            var result = new List<UfsDirent>();
            int pos = 0;
            while (pos + UfsConstants.DirEntryHeader <= data.Length)
            {
                uint entryIno;
                if (_sb.BigEndian)
                {
                    entryIno = (uint)((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);
                }
                else
                {
                    entryIno = (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24));
                }
                int reclen = _sb.BigEndian ? (data[pos + 4] << 8) | data[pos + 5] : data[pos + 4] | (data[pos + 5] << 8);
                byte dtype = data[pos + 6];
                int namlen = data[pos + 7];
                if (reclen < UfsConstants.DirEntryHeader || pos + reclen > data.Length)
                {
                    break;
                }
                if (namlen < 0 || namlen > UfsConstants.MaxNameLength || 8 + namlen > reclen)
                {
                    break;
                }
                if (entryIno != 0 && namlen > 0)
                {
                    var dirent = new UfsDirent();
                    dirent.Ino = entryIno;
                    dirent.Dtype = dtype;
                    dirent.Name = Encoding.UTF8.GetString(data, pos + 8, namlen);
                    result.Add(dirent);
                }
                pos += reclen;
            }
            return result;
        }

        public byte[] ReadSymlink(uint ino)
        {
            UfsInode inode = GetInode(ino);
            if (!inode.IsSymlink)
            {
                throw new IOException("ufs not a symlink: " + ino);
            }
            if (inode.Size <= _sb.MaxSymlinkLen && inode.Size <= (ulong)inode.Shortlink.Length)
            {
                var target = new byte[(int)inode.Size];
                Array.Copy(inode.Shortlink, target, (int)inode.Size);
                return target;
            }
            return ReadFile(ino);
        }
    }
}
