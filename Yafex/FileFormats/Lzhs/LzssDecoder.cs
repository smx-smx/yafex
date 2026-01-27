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

        private RingBuffer<byte> ringBuf = new RingBuffer<byte>(N + F - 1, N - 1u);
        private ModCounter bufPos = new ModCounter(0, N - 1u);

        private IEnumerator<byte> it;

        public LzssDecoder(IEnumerable<byte> data)
        {
            ringBuf.Fill(0);
            it = data.GetEnumerator();
        }

        private LzssSequenceType[] ReadSequence(byte flag)
        {
            var seq = new LzssSequenceType[8];

            // shift register
            byte shr = flag;

            for (int i = 0; i < 8; i++)
            {
                seq[i] = (shr & 1u) switch
                {
                    0 => LzssSequenceType.POINTER,
                    1 => LzssSequenceType.RAW
                };
                shr >>= 1;
            }

            return seq;
        }

        private bool TryReadByte(out byte b)
        {
            if (!it.MoveNext())
            {
                b = 0;
                return false;
            }
            b = it.Current;
            return true;
        }

        private IEnumerable<byte> DecodeRaw()
        {
            if (!TryReadByte(out byte b))
            {
                yield break;
            }
            ringBuf[bufPos++.value] = b;
            yield return b;
        }

        private IEnumerable<byte> DecodePointer()
        {
            if (!TryReadByte(out byte matchLen)) yield break;
            if (!TryReadByte(out byte b1)) yield break;
            if (!TryReadByte(out byte b0)) yield break;

            var matchPos = (b1 << 8) | b0;
            for (int i = 0; i <= matchLen + THRESHOLD; i++)
            {
                byte b = ringBuf[(bufPos - matchPos).value];
                ringBuf[bufPos++.value] = b;
                yield return b;
            }
        }

        public IEnumerable<byte> Decode()
        {
            while (TryReadByte(out byte flags))
            {
                foreach (var i in ReadSequence(flags))
                {
                    switch (i)
                    {
                        case LzssSequenceType.POINTER:
                            foreach (var b in DecodePointer()) yield return b;
                            break;
                        case LzssSequenceType.RAW:
                            foreach (var b in DecodeRaw()) yield return b;
                            break;
                    }
                }
            }
        }
    }
}
