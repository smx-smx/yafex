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
    public class LzssDecoder
    {
        const int N = 4096;
        const int F = 34;
        const int THRESHOLD = 2;

        private readonly byte[] data;

        public LzssDecoder(IEnumerable<byte> data)
        {
            // Materialize once: the old path chained per-byte iterators
            // (MoveNext + yield per output byte) through this stage.
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

        public byte[] DecodeToArray(int capacityHint = 0)
        {
            var ring = new byte[N];
            int bufPos = 0;
            var outBuf = new List<byte>(capacityHint > 0 ? capacityHint : data.Length);
            int pos = 0;

            while (pos < data.Length)
            {
                byte flags = data[pos++];
                for (int i = 0; i < 8; i++)
                {
                    if (((flags >> i) & 1u) == 0)
                    {
                        // POINTER: length byte + 2 position bytes
                        if (pos + 3 > data.Length) return outBuf.ToArray();
                        int matchLen = data[pos];
                        int matchPos = (data[pos + 1] << 8) | data[pos + 2];
                        pos += 3;
                        for (int k = 0; k <= matchLen + THRESHOLD; k++)
                        {
                            byte b = ring[(bufPos - matchPos) & (N - 1)];
                            ring[bufPos] = b;
                            bufPos = (bufPos + 1) & (N - 1);
                            outBuf.Add(b);
                        }
                    }
                    else
                    {
                        // RAW
                        if (pos >= data.Length) return outBuf.ToArray();
                        byte b = data[pos++];
                        ring[bufPos] = b;
                        bufPos = (bufPos + 1) & (N - 1);
                        outBuf.Add(b);
                    }
                }
            }
            return outBuf.ToArray();
        }

        public IEnumerable<byte> Decode()
        {
            return DecodeToArray();
        }
    }
}
