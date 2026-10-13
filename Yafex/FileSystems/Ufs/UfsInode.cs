using System;
using System.IO;

namespace Yafex.FileSystems.Ufs
{
    // dinode layouts from FreeBSD sys/ufs/ufs/dinode.h.
    // UFS1 (128 bytes): mode@0 nlink@2 size@8 mtime@24 db[12]@40 ib[3]@88
    // shortlink@40 (60 bytes). UFS2 (256 bytes): mode@0 nlink@2 size@16
    // mtime@40 db[12]@112 ib[3]@208 shortlink@112 (120 bytes).
    public sealed class UfsInode
    {
        public uint Ino;
        public uint Mode;
        public ulong Size;
        public long Mtime;
        public ulong[] Direct = new ulong[UfsConstants.DirectCount];
        public ulong[] Indirect = new ulong[UfsConstants.IndirectCount];
        public byte[] Shortlink = new byte[0];

        public bool IsDirectory
        {
            get { return (Mode & UfsConstants.ModeTypeMask) == UfsConstants.ModeDirectory; }
        }

        public bool IsSymlink
        {
            get { return (Mode & UfsConstants.ModeTypeMask) == UfsConstants.ModeSymlink; }
        }

        public bool IsFile
        {
            get { return (Mode & UfsConstants.ModeTypeMask) == UfsConstants.ModeFile; }
        }

        public static UfsInode Read(byte[] raw, uint ino, bool isUfs2, bool bigEndian)
        {
            var inode = new UfsInode();
            inode.Ino = ino;
            inode.Mode = UfsConstants.ReadU16(raw, 0, bigEndian);
            if (isUfs2)
            {
                if (raw.Length < 256)
                {
                    throw new IOException("ufs2 inode too short");
                }
                inode.Size = UfsConstants.ReadU64(raw, 16, bigEndian);
                inode.Mtime = (long)UfsConstants.ReadU64(raw, 40, bigEndian);
                for (int i = 0; i < UfsConstants.DirectCount; i++)
                {
                    inode.Direct[i] = UfsConstants.ReadU64(raw, 112 + i * 8, bigEndian);
                }
                for (int i = 0; i < UfsConstants.IndirectCount; i++)
                {
                    inode.Indirect[i] = UfsConstants.ReadU64(raw, 208 + i * 8, bigEndian);
                }
                inode.Shortlink = new byte[120];
                Array.Copy(raw, 112, inode.Shortlink, 0, 120);
            }
            else
            {
                if (raw.Length < 128)
                {
                    throw new IOException("ufs1 inode too short");
                }
                inode.Size = UfsConstants.ReadU64(raw, 8, bigEndian);
                inode.Mtime = UfsConstants.ReadU32(raw, 24, bigEndian);
                for (int i = 0; i < UfsConstants.DirectCount; i++)
                {
                    inode.Direct[i] = UfsConstants.ReadU32(raw, 40 + i * 4, bigEndian);
                }
                for (int i = 0; i < UfsConstants.IndirectCount; i++)
                {
                    inode.Indirect[i] = UfsConstants.ReadU32(raw, 88 + i * 4, bigEndian);
                }
                inode.Shortlink = new byte[60];
                Array.Copy(raw, 40, inode.Shortlink, 0, 60);
            }
            return inode;
        }
    }
}
