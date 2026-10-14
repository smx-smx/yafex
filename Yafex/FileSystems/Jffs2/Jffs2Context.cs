using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.Jffs2
{
    /// <summary>
    /// One dirent: name link (parent, name) -&gt; child inode, latest version wins.
    /// </summary>
    public sealed class Jffs2Dirent
    {
        public uint Parent;
        public uint Ino;
        public uint Version;
        public byte Dtype;
        public string Name = string.Empty;
    }

    /// <summary>
    /// One inode data node: a versioned fragment of a file's payload.
    /// </summary>
    public sealed class Jffs2DataNode
    {
        public uint Version;
        public uint Offset;
        public uint CompSize;
        public uint Size;
        public byte Compr;
        public long DataPos;
    }

    public class Jffs2Inode
    {
        public uint Ino;
        public uint Mode;
        public uint Uid;
        public uint Gid;
        public uint Mtime;
        public uint Size;
        internal uint VersionSeen;
    }

    /// <summary>
    /// Read-only JFFS2 engine: linear node scan, latest-version dirents,
    /// version-painted fragment assembly, NONE+zlib payloads.
    /// Node CRCs are NOT enforced (observed writers emit non-IEEE values);
    /// structure plus zlib integrity decide validity.
    /// </summary>
    public sealed class Jffs2Context : VfsContext
    {
        private readonly Stream _stream;

        private readonly Dictionary<uint, Jffs2Inode> _inodes = new Dictionary<uint, Jffs2Inode>();
        private readonly Dictionary<string, Jffs2Dirent> _dirents = new Dictionary<string, Jffs2Dirent>();
        private readonly Dictionary<uint, List<Jffs2DataNode>> _data = new Dictionary<uint, List<Jffs2DataNode>>();

        private Jffs2Context(Stream stream)
        {
            _stream = stream;
        }

        public static Jffs2Context Open(Stream stream)
        {
            if (stream == null || !stream.CanSeek)
            {
                throw new IOException("jffs2 needs a seekable stream");
            }
            var ctx = new Jffs2Context(stream);
            ctx.Scan();
            if (ctx._inodes.Count == 0 || ctx._dirents.Count == 0)
            {
                throw new IOException("no jffs2 nodes found");
            }
            return ctx;
        }

        private static string DirentKey(uint parent, string name)
        {
            return parent + "/" + name;
        }

        private void Scan()
        {
            long len = _stream.Length;
            long pos = 0;
            while (pos + 12 <= len)
            {
                byte[] head = Jffs2Constants.ReadBytes(_stream, pos, 12);
                if (Jffs2Constants.ReadU16(head, 0) != Jffs2Constants.Magic)
                {
                    pos += 4;
                    continue;
                }
                int type = Jffs2Constants.ReadU16(head, 2) & 0x0FFF;
                long totlen = Jffs2Constants.ReadU32(head, 4);
                if (totlen < 12 || pos + totlen > len)
                {
                    pos += 4;
                    continue;
                }
                if (type == Jffs2Constants.TypeDirent)
                {
                    if (totlen < Jffs2Constants.DirentSize)
                    {
                        pos += 4;
                        continue;
                    }
                    byte[] raw = Jffs2Constants.ReadBytes(_stream, pos, (int)Math.Min(totlen, Jffs2Constants.DirentSize + 256));
                    uint parent = Jffs2Constants.ReadU32(raw, 12);
                    uint version = Jffs2Constants.ReadU32(raw, 16);
                    uint ino = Jffs2Constants.ReadU32(raw, 20);
                    int nsize = raw[28];
                    byte dtype = raw[29];
                    if (40 + nsize > raw.Length)
                    {
                        pos += 4;
                        continue;
                    }
                    string name = Encoding.ASCII.GetString(raw, 40, nsize);
                    string key = DirentKey(parent, name);
                    if (!_dirents.TryGetValue(key, out var cur) || version >= cur.Version)
                    {
                        _dirents[key] = new Jffs2Dirent
                        {
                            Parent = parent,
                            Ino = ino,
                            Version = version,
                            Dtype = dtype,
                            Name = name
                        };
                    }
                    EnsureInode(ino);
                }
                else if (type == Jffs2Constants.TypeInode)
                {
                    if (totlen < Jffs2Constants.InodeSize)
                    {
                        pos += 4;
                        continue;
                    }
                    byte[] raw = Jffs2Constants.ReadBytes(_stream, pos, Jffs2Constants.InodeSize);
                    uint ino = Jffs2Constants.ReadU32(raw, 12);
                    uint version = Jffs2Constants.ReadU32(raw, 16);
                    uint mode = Jffs2Constants.ReadU32(raw, 20);
                    uint uid = Jffs2Constants.ReadU16(raw, 24);
                    uint gid = Jffs2Constants.ReadU16(raw, 26);
                    uint isize = Jffs2Constants.ReadU32(raw, 28);
                    uint mtime = Jffs2Constants.ReadU32(raw, 36);
                    uint offset = Jffs2Constants.ReadU32(raw, 44);
                    uint csize = Jffs2Constants.ReadU32(raw, 48);
                    uint dsize = Jffs2Constants.ReadU32(raw, 52);
                    byte compr = raw[56];
                    if (pos + Jffs2Constants.InodeSize + csize > len)
                    {
                        pos += 4;
                        continue;
                    }
                    var inode = EnsureInode(ino);
                    if (version >= inode.VersionSeen)
                    {
                        inode.Mode = mode;
                        inode.Uid = uid;
                        inode.Gid = gid;
                        inode.Mtime = mtime;
                        inode.Size = isize;
                        inode.VersionSeen = version;
                    }
                    if (!_data.TryGetValue(ino, out var list))
                    {
                        list = new List<Jffs2DataNode>();
                        _data[ino] = list;
                    }
                    list.Add(new Jffs2DataNode
                    {
                        Version = version,
                        Offset = offset,
                        CompSize = csize,
                        Size = dsize,
                        Compr = compr,
                        DataPos = pos + Jffs2Constants.InodeSize
                    });
                }
                else if (type != Jffs2Constants.TypeCleanmarker && type != Jffs2Constants.TypePadding)
                {
                    pos += 4;
                    continue;
                }
                pos += (totlen + 3) & ~3L;
            }
        }

        private Jffs2Inode EnsureInode(uint ino)
        {
            if (!_inodes.TryGetValue(ino, out var inode))
            {
                inode = new Jffs2Inode { Ino = ino };
                _inodes[ino] = inode;
            }
            return inode;
        }

        public Jffs2Inode GetInode(uint ino)
        {
            if (!_inodes.TryGetValue(ino, out var inode))
            {
                throw new IOException("jffs2 inode not found: " + ino);
            }
            return inode;
        }

        public List<Jffs2Dirent> ReadDirectory(uint ino)
        {
            var result = new List<Jffs2Dirent>();
            foreach (var e in _dirents.Values)
            {
                if (e.Parent == ino)
                {
                    result.Add(e);
                }
            }
            return result;
        }

        private static byte[] Inflate(byte[] compressed, long expected)
        {
            int start = compressed.Length >= 2 && compressed[0] == 0x78 ? 2 : 0;
            using (var raw = new MemoryStream(compressed, start, compressed.Length - start, false))
            using (var inflater = new DeflateStream(raw, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buf = new byte[1 << 16];
                long total = 0;
                for (;;)
                {
                    int n = inflater.Read(buf, 0, buf.Length);
                    if (n <= 0)
                    {
                        break;
                    }
                    total += n;
                    if (total > expected)
                    {
                        throw new IOException("jffs2 inflate overrun");
                    }
                    output.Write(buf, 0, n);
                }
                byte[] result = output.ToArray();
                if (result.Length != expected)
                {
                    throw new IOException("jffs2 block did not inflate to its size");
                }
                return result;
            }
        }

        private byte[] NodePayload(Jffs2DataNode node)
        {
            byte[] stored = Jffs2Constants.ReadBytes(_stream, node.DataPos, (int)node.CompSize);
            if (node.Compr == Jffs2Constants.ComprNone)
            {
                if (node.CompSize != node.Size)
                {
                    throw new IOException("jffs2 uncompressed size mismatch");
                }
                return stored;
            }
            if (node.Compr == Jffs2Constants.ComprZlib)
            {
                return Inflate(stored, node.Size);
            }
            throw new IOException("unsupported jffs2 compression: " + node.Compr);
        }

        /// <summary>Assembled file content: higher versions paint over lower.</summary>
        public byte[] ReadFile(uint ino)
        {
            var inode = GetInode(ino);
            if (inode.Size == 0)
            {
                return new byte[0];
            }
            if (inode.Size > (uint)_stream.Length)
            {
                throw new IOException("jffs2 file too large");
            }
            if (!_data.TryGetValue(ino, out var nodes))
            {
                return new byte[inode.Size];
            }
            var ordered = new List<Jffs2DataNode>(nodes);
            ordered.Sort((a, b) => a.Version.CompareTo(b.Version));
            var output = new byte[inode.Size];
            foreach (var node in ordered)
            {
                if (node.Offset >= output.Length)
                {
                    continue;
                }
                byte[] payload = NodePayload(node);
                long take = Math.Min(payload.Length, output.Length - node.Offset);
                // holes stay zero-filled
                Array.Copy(payload, 0, output, node.Offset, take);
            }
            return output;
        }
    }
}
