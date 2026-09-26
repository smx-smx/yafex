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
    public class ArmThumbConvert
    {
        private readonly byte[] data;

        public ArmThumbConvert(IEnumerable<byte> data)
        {
            // Materialize once: the old path allocated a byte[2] per
            // instruction and yielded every byte through an iterator.
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

        private static bool IsValidThumbInsnSeq0(byte hi)
        {
            return (hi & 0xF8) == 0xF0;
        }

        private static bool IsValidThumbInsnSeq1(byte hi)
        {
            return (hi & 0xF8) == 0xF8;
        }

        private static void TransformThumbPair(
            byte i10, byte i11, byte i20, byte i21,
            uint nowPos, uint curPos, bool encoding,
            byte[] output, int outPos)
        {
            var src = (
                ((i11 & 0x7) << 19)
                | (i10 << 11)
                | ((i21 & 0x7) << 8)
                | i20
            ) << 1;

            var dest = encoding switch
            {
                true => nowPos + curPos + 4 + src,
                false => src - (nowPos + curPos + 4)
            } >> 1;

            output[outPos] = (byte)(dest >> 11);
            output[outPos + 1] = (byte)(0xF0 | ((dest >> 19) & 0x7));
            output[outPos + 2] = (byte)dest;
            output[outPos + 3] = (byte)(0xF8 | (dest >> 8) & 0x7);
        }

        private byte[] ConvertToArray(uint nowPos, bool encoding)
        {
            var output = new byte[data.Length];
            int outPos = 0;
            uint pos = 0;
            int i = 0;
            while (i < data.Length)
            {
                int remaining = data.Length - i;
                if (remaining < 2)
                {
                    output[outPos++] = data[i++];
                    break;
                }
                if (remaining < 4)
                {
                    output[outPos++] = data[i++];
                    output[outPos++] = data[i++];
                    break;
                }

                byte b0 = data[i], b1 = data[i + 1], b2 = data[i + 2], b3 = data[i + 3];
                if (IsValidThumbInsnSeq0(b1)
                    && IsValidThumbInsnSeq1(b3)
                )
                {
                    TransformThumbPair(b0, b1, b2, b3, nowPos, pos, encoding, output, outPos);
                    outPos += 4;
                    i += 4;
                    pos += 4;
                }
                else
                {
                    output[outPos++] = b0;
                    output[outPos++] = b1;
                    i += 2;
                    pos += 2;
                }
            }
            if (outPos == output.Length) return output;
            var trimmed = new byte[outPos];
            System.Array.Copy(output, trimmed, outPos);
            return trimmed;
        }

        public byte[] DecodeToArray(uint nowPos)
        {
            return ConvertToArray(nowPos, false);
        }

        public IEnumerable<byte> Decode(uint nowPos)
        {
            return DecodeToArray(nowPos);
        }
    }
}
