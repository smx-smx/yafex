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
using System.Collections.Generic;

namespace Yafex.FileFormats.Lzhs
{
    public class HuffmanDecoder
    {
        private readonly byte[] data;

        public HuffmanDecoder(IEnumerable<byte> data)
        {
            // Materialize once: the old path pulled bits through a per-byte
            // enumerator (MoveNext per bit); bulk input removes that overhead.
            if (data is byte[] arr)
            {
                this.data = arr;
            }
            else
            {
                var buf = new List<byte>();
                foreach (var b in data) buf.Add(b);
                this.data = buf.ToArray();
            }
        }

        private sealed class BitReader
        {
            private readonly byte[] buf;
            private readonly int end;
            private int bytePos;
            private int bitPos;

            public BitReader(byte[] buf, int start, int length)
            {
                this.buf = buf;
                this.bytePos = start;
                this.end = start + length;
                this.bitPos = 0;
            }

            // MSB first, matching the old BitStream mode
            public bool TryReadBit(out int bit)
            {
                if (bytePos >= end)
                {
                    bit = 0;
                    return false;
                }
                bit = (buf[bytePos] >> (7 - bitPos)) & 1;
                if (++bitPos == 8)
                {
                    bitPos = 0;
                    bytePos++;
                }
                return true;
            }
        }

        /// <summary>Binary Huffman decode tree: one array access per bit,
        /// no hashing, no per-symbol allocations. Bit order matches the old
        /// MSB-first accumulator over the same (code, length) tables.</summary>
        private sealed class DecodeTree
        {
            // edges[2 * node] = child on 0, edges[2 * node + 1] = child on 1
            // (0 = absent); leaves[2 * node] = -(symbol + 1) when present.
            private int[] edges = new int[2];
            private int[] leaves = new int[2];
            private int count = 1;

            public DecodeTree(HuffmanSymbol[] table)
            {
                for (int idx = 0; idx < table.Length; idx++)
                {
                    var sym = table[idx];
                    int node = 0;
                    for (int i = sym.length - 1; i >= 0; i--)
                    {
                        int bit = (int)((sym.code >> i) & 1u);
                        int edge = node * 2 + bit;
                        Ensure(edge);
                        if (edges[edge] == 0)
                        {
                            edges[edge] = count++;
                        }
                        node = edges[edge];
                        Ensure(node * 2 + 1);
                    }
                    Ensure(node * 2);
                    leaves[node * 2] = -(idx + 1);
                }
            }

            private void Ensure(int idx)
            {
                if (idx < edges.Length) return;
                int size = edges.Length;
                while (size <= idx) size *= 2;
                var e2 = new int[size];
                var l2 = new int[size];
                edges.CopyTo(e2, 0);
                leaves.CopyTo(l2, 0);
                edges = e2;
                leaves = l2;
            }

            public bool TryDecode(BitReader reader, out int symbol)
            {
                int node = 0;
                while (true)
                {
                    if (!reader.TryReadBit(out int bit))
                    {
                        symbol = 0;
                        return false;
                    }
                    int edge = node * 2 + bit;
                    if (edge >= edges.Length || edges[edge] == 0)
                    {
                        symbol = 0;
                        return false;
                    }
                    node = edges[edge];
                    int leaf = node * 2 < leaves.Length ? leaves[node * 2] : 0;
                    if (leaf < 0)
                    {
                        symbol = -(leaf + 1);
                        return true;
                    }
                }
            }
        }

        private static readonly DecodeTree CharLenTree = new DecodeTree(LzhsTables.TblCharlen);
        private static readonly DecodeTree CharPosTree = new DecodeTree(LzhsTables.TblCharpos);

        private static bool TryGetNextItem(BitReader reader, List<byte> buf, out LzssSequenceType item)
        {
            item = default;

            if (!CharLenTree.TryDecode(reader, out int codePoint))
            {
                return false;
            }
            // if the codepoint lies within a byte range, it's raw data
            // if it lies outside, it's an lzss length element
            if (codePoint > 255)
            {
                // write length of LZSS item
                var lzssLength = codePoint - 256;
                buf.Add((byte)lzssLength);

                // read huffman encoded byte1 position
                if (!CharPosTree.TryDecode(reader, out int lzssPos1))
                {
                    return false;
                }
                buf.Add((byte)(lzssPos1 >> 1));

                int lzssPos0 = 0;
                for (int i = 0; i < 7; i++)
                {
                    if (!reader.TryReadBit(out int bit))
                    {
                        return false;
                    }
                    lzssPos0 = (lzssPos0 << 1) | bit;
                }
                lzssPos0 |= lzssPos1 << 7;

                buf.Add((byte)(
                    (lzssPos1 << 7) | lzssPos0
                ));

                item = LzssSequenceType.POINTER;
            }
            else
            {
                // raw data
                buf.Add((byte)codePoint);
                item = LzssSequenceType.RAW;
            }
            return true;
        }

        public byte[] DecodeToArray()
        {
            var reader = new BitReader(data, 0, data.Length);
            var outBuf = new List<byte>(data.Length * 2);
            // worst case: 8 sequences of pointers (3 bytes each);
            // flag is excluded (written separately)
            var itemBuf = new List<byte>(24);

            bool stop = false;
            while (!stop)
            {
                byte flags = 0;
                itemBuf.Clear();
                for (int i = 0; i < 8; i++)
                {
                    if (!TryGetNextItem(reader, itemBuf, out var item))
                    {
                        stop = true;
                        break;
                    }
                    flags |= (byte)((byte)item << i);
                }

                /** flush buffer **/
                // write flag
                outBuf.Add(flags);
                // write data
                outBuf.AddRange(itemBuf);
            }
            return outBuf.ToArray();
        }

        public IEnumerable<byte> Decode()
        {
            return DecodeToArray();
        }
    }
}
