using System;
using System.Collections.Generic;
using System.Text;

namespace Yafex.FileSystems.Vdfs
{
    /// <summary>
    /// Catalog/extents b-tree walk over 16KB metadata nodes. Record addressing
    /// follows the on-disk offset table at each node's tail; index records
    /// carry the child node id right after the key.
    /// </summary>
    internal sealed class VdfsBtree
    {
        private readonly VdfsContext _ctx;
        private readonly ulong _ino;
        private readonly VdfsBtreeHead _head;

        public VdfsBtree(VdfsContext ctx, ulong ino)
        {
            _ctx = ctx;
            _ino = ino;
            _head = ctx.GetTreeHead(ino);
        }

        private sealed class Node
        {
            public uint Id;
            public uint Prev;
            public uint Next;
            public uint Type;
            public ushort Recs;
            public uint[] Offsets = new uint[0];
            public byte[] Data = new byte[0];
        }

        private Node ReadNode(uint nodeId)
        {
            byte[] raw = _ctx.ReadNode(_ino, nodeId);
            if (raw.Length < VdfsConstants.NodeDescrSize)
            {
                throw new System.IO.IOException("VDFS node too small");
            }
            var node = new Node
            {
                Id = VdfsConstants.ReadU32(raw, VdfsConstants.NodeIdOff),
                Prev = VdfsConstants.ReadU32(raw, VdfsConstants.NodePrevOff),
                Next = VdfsConstants.ReadU32(raw, VdfsConstants.NodeNextOff),
                Type = VdfsConstants.ReadU32(raw, VdfsConstants.NodeTypeOff),
                Recs = VdfsConstants.ReadU16(raw, VdfsConstants.NodeRecsOff),
                Data = raw
            };
            node.Offsets = new uint[node.Recs + 1];
            for (int i = 0; i <= node.Recs; i++)
            {
                int at = raw.Length - VdfsConstants.NodeTailReserved - 4 * (i + 1);
                node.Offsets[i] = at >= 0 ? VdfsConstants.ReadU32(raw, at) : 0;
            }
            return node;
        }

        private static byte[] GetRecord(Node node, int index)
        {
            int off = (int)node.Offsets[index];
            int avail = node.Data.Length - off;
            if (avail < 8 || off < 0)
            {
                return new byte[0];
            }
            int recLen = VdfsConstants.ReadU16(node.Data, off + VdfsConstants.RecordLenOff);
            if (recLen > avail)
            {
                recLen = avail;
            }
            var rec = new byte[recLen];
            Array.Copy(node.Data, off, rec, 0, recLen);
            return rec;
        }

        private static int CompareCatKey(ulong parent, byte[] name, ulong obj, byte[] stored)
        {
            if (stored.Length < VdfsConstants.KeyNameOff)
            {
                return -1;
            }
            ulong sp = VdfsConstants.ReadU64(stored, VdfsConstants.KeyParentOff);
            if (parent != sp)
            {
                return parent < sp ? -1 : 1;
            }
            int slen = stored[VdfsConstants.KeyNameLenOff];
            int nlen = name.Length < VdfsConstants.MaxNameLen ? name.Length : VdfsConstants.MaxNameLen;
            int common = slen < nlen ? slen : nlen;
            for (int i = 0; i < common; i++)
            {
                byte a = name[i];
                byte b = stored[VdfsConstants.KeyNameOff + i];
                if (a != b)
                {
                    return a < b ? -1 : 1;
                }
            }
            if (nlen != slen)
            {
                return nlen < slen ? -1 : 1;
            }
            ulong so = VdfsConstants.ReadU64(stored, VdfsConstants.KeyObjectOff);
            if (obj != so)
            {
                return obj < so ? -1 : 1;
            }
            return 0;
        }

        public static VdfsCatRecord ParseRecord(byte[] raw)
        {
            if (raw.Length < VdfsConstants.KeyNameOff)
            {
                return null;
            }
            int keyLen = VdfsConstants.ReadU16(raw, VdfsConstants.KeyLenOff);
            int recLen = VdfsConstants.ReadU16(raw, VdfsConstants.RecordLenOff);
            if (keyLen < VdfsConstants.KeyNameOff || keyLen > raw.Length || recLen > raw.Length
                || recLen < keyLen)
            {
                return null;
            }
            int nameLen = raw[VdfsConstants.KeyNameLenOff];
            if (VdfsConstants.KeyNameOff + nameLen > keyLen)
            {
                return null;
            }
            var rec = new VdfsCatRecord
            {
                ParentId = VdfsConstants.ReadU64(raw, VdfsConstants.KeyParentOff),
                ObjectId = VdfsConstants.ReadU64(raw, VdfsConstants.KeyObjectOff),
                Type = raw[VdfsConstants.KeyTypeOff],
                Name = Encoding.UTF8.GetString(raw, VdfsConstants.KeyNameOff, nameLen)
            };
            var value = new byte[recLen - keyLen];
            Array.Copy(raw, keyLen, value, 0, value.Length);
            rec.Value = value;
            return rec;
        }

        private Node FindLeaf(ulong parent, byte[] name, ulong obj)
        {
            Node node = ReadNode(_head.Root);
            for (int level = 0; level < _head.Height; level++)
            {
                if (node.Recs == 0)
                {
                    throw new System.IO.IOException("VDFS empty b-tree node");
                }
                int low = 0;
                int high = node.Recs;
                int diff = -1;
                int index = 0;
                while (low < high)
                {
                    int mid = low + (high - low) / 2;
                    int cmp = CompareCatKey(parent, name, obj, GetRecord(node, mid));
                    if (cmp == 0)
                    {
                        diff = 0;
                        index = mid;
                        break;
                    }
                    if (cmp > 0)
                    {
                        diff = 1;
                        low = mid + 1;
                    }
                    else
                    {
                        diff = -1;
                        high = mid;
                    }
                }
                if (diff != 0)
                {
                    index = low;
                }
                if (node.Type == VdfsConstants.NodeLeaf)
                {
                    return node;
                }
                int child;
                if (diff == 0)
                {
                    child = index;
                }
                else if (index > 0)
                {
                    child = index - 1;
                }
                else
                {
                    child = 0;
                }
                byte[] rec = GetRecord(node, child);
                if (rec.Length < 8)
                {
                    throw new System.IO.IOException("VDFS bad index record");
                }
                int keyLen = VdfsConstants.ReadU16(rec, VdfsConstants.KeyLenOff);
                if (keyLen + 4 > rec.Length)
                {
                    throw new System.IO.IOException("VDFS bad index record");
                }
                node = ReadNode(VdfsConstants.ReadU32(rec, keyLen));
            }
            throw new System.IO.IOException("VDFS b-tree walk fell off the tree");
        }

        // Leaf index where (parent, name, obj) would land; the caller scans
        // forward while the parent matches.
        private static int LeafPosition(Node node, ulong parent, byte[] name, ulong obj)
        {
            int low = 0;
            int high = node.Recs;
            while (low < high)
            {
                int mid = low + (high - low) / 2;
                int cmp = CompareCatKey(parent, name, obj, GetRecord(node, mid));
                if (cmp == 0)
                {
                    return mid;
                }
                if (cmp > 0)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }
            return low;
        }

        /// <summary>
        /// All records whose parent is <paramref name="parentId"/>, in key
        /// order (leaf linked-list walk from the lower bound).
        /// </summary>
        public List<VdfsCatRecord> Children(ulong parentId)
        {
            var result = new List<VdfsCatRecord>();
            Node node = FindLeaf(parentId, new byte[0], 0);
            int index = LeafPosition(node, parentId, new byte[0], 0);
            if (index >= node.Recs && node.Next != 0)
            {
                node = ReadNode(node.Next);
                index = 0;
            }
            while (true)
            {
                while (index < node.Recs)
                {
                    VdfsCatRecord rec = ParseRecord(GetRecord(node, index));
                    index++;
                    if (rec == null)
                    {
                        continue;
                    }
                    if (rec.ParentId != parentId)
                    {
                        return result;
                    }
                    result.Add(rec);
                }
                if (node.Next == 0)
                {
                    break;
                }
                node = ReadNode(node.Next);
                index = 0;
            }
            return result;
        }

        public VdfsCatRecord FindChild(ulong parentId, string name)
        {
            // Links (hard/internal) may share their (parent, name) key with
            // the real record (e.g. usd/usd -> lib next to a real usd dir).
            // Prefer the real record so traversal never enters links (which
            // would cycle back up); fall back to the link when it stands
            // alone. This inverts the driver's shadowing order, deliberately:
            // the driver's link-first order is unmappable without cycles.
            VdfsCatRecord found = ScanChild(parentId, name, false);
            if (found == null)
            {
                found = ScanChild(parentId, name, true);
            }
            return found;
        }

        private VdfsCatRecord ScanChild(ulong parentId, string name, bool includeLinks)
        {
            byte[] key = Encoding.UTF8.GetBytes(name);
            Node node = FindLeaf(parentId, key, 0);
            int index = LeafPosition(node, parentId, key, 0);
            if (index >= node.Recs && node.Next != 0)
            {
                node = ReadNode(node.Next);
                index = 0;
            }
            while (true)
            {
                while (index < node.Recs)
                {
                    VdfsCatRecord rec = ParseRecord(GetRecord(node, index));
                    index++;
                    if (rec == null)
                    {
                        continue;
                    }
                    if (rec.ParentId != parentId)
                    {
                        return null;
                    }
                    if (rec.Name == name && (includeLinks || !IsLink(rec.Type)))
                    {
                        return rec;
                    }
                }
                if (node.Next == 0)
                {
                    break;
                }
                node = ReadNode(node.Next);
                index = 0;
            }
            return null;
        }

        private static bool IsLink(byte type)
        {
            return type == VdfsConstants.RecHLink || type == VdfsConstants.RecILink;
        }

        /// <summary>Every leaf record in the tree (for object-id indexing).</summary>
        public List<byte[]> AllLeafRecords()
        {
            var result = new List<byte[]>();
            Node node = ReadNode(_head.Root);
            for (int level = 0; level + 1 < _head.Height; level++)
            {
                if (node.Recs == 0)
                {
                    return result;
                }
                byte[] rec = GetRecord(node, 0);
                int keyLen = VdfsConstants.ReadU16(rec, VdfsConstants.KeyLenOff);
                node = ReadNode(VdfsConstants.ReadU32(rec, keyLen));
            }
            while (true)
            {
                for (int i = 0; i < node.Recs; i++)
                {
                    result.Add(GetRecord(node, i));
                }
                if (node.Next == 0)
                {
                    break;
                }
                node = ReadNode(node.Next);
            }
            return result;
        }
    }
}
