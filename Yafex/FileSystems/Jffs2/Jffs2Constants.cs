using System;
using System.IO;

namespace Yafex.FileSystems.Jffs2
{
    internal static class Jffs2Constants
    {
        public const ushort Magic = 0x1985;

        // Base nodetypes (high bits carry compat flags).
        public const int TypeDirent = 1;
        public const int TypeInode = 2;
        public const int TypeCleanmarker = 3;
        public const int TypePadding = 4;

        public const int DirentSize = 40;
        public const int InodeSize = 68;

        // Payload compression.
        public const byte ComprNone = 0;
        public const byte ComprZlib = 6;

        // Dirent dtype.
        public const byte DtypeDir = 4;
        public const byte DtypeReg = 8;
        public const byte DtypeSymlink = 10;

        public const int ModeTypeMask = 0xF000;

        public static ushort ReadU16(byte[] buf, long off)
        {
            return (ushort)(buf[off] | (buf[off + 1] << 8));
        }

        public static uint ReadU32(byte[] buf, long off)
        {
            return (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));
        }

        public static byte[] ReadBytes(Stream stream, long offset, int count)
        {
            if (offset < 0 || offset + count > stream.Length)
            {
                throw new IOException("jffs2 read out of range");
            }
            long saved = stream.Position;
            try
            {
                stream.Position = offset;
                var buf = new byte[count];
                int done = 0;
                while (done < count)
                {
                    int n = stream.Read(buf, done, count - done);
                    if (n <= 0)
                    {
                        throw new IOException("jffs2 short read");
                    }
                    done += n;
                }
                return buf;
            }
            finally
            {
                stream.Position = saved;
            }
        }
    }
}
