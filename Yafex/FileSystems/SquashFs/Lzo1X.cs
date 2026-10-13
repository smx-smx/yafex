//
// Copyright (c) 2026, Stefano Moioli
//
// LZO1X-1 safe decompressor for the LZO1X block format
// (original algorithm by Markus F.X.J. Oberhumer). Validated block by block
// against the reference liblzo implementation.
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
using System.IO;

namespace Yafex.FileSystems.SquashFs
{
    internal static class Lzo1X
    {
        /// <summary>
        /// Decompresses one LZO1X block. Throws <see cref="IOException"/> on
        /// corrupt input or destination overrun (safe-decoder semantics).
        /// </summary>
        /// <returns>Number of decompressed bytes written.</returns>
        public static int Decompress(byte[] input, int inOffset, int inLength, byte[] output, int outOffset, int maxOutputLength)
        {
            int ipEnd = inOffset + inLength;
            int opEnd = outOffset + maxOutputLength;
            int ip = inOffset;
            int op = outOffset;
            int t;

            if (inLength < 1)
            {
                throw new IOException("LZO stream is empty");
            }

            bool entered = false;
            for (;;)
            {
            outer_next:
                // Outer position: literal run or match, never M1.
                if (!entered)
                {
                    entered = true;
                    t = input[ip++];
                    if (t > 17)
                    {
                        // Stream entry: leading literals of length t - 17.
                        int litLen = t - 17;
                        if (ip + litLen > ipEnd || op + litLen > opEnd)
                        {
                            throw new IOException("LZO leading literals overrun input or output");
                        }
                        Array.Copy(input, ip, output, op, litLen);
                        ip += litLen;
                        op += litLen;
                        if (litLen < 4)
                        {
                            if (ip >= ipEnd)
                            {
                                throw new IOException("LZO stream ended after leading literals");
                            }
                            t = input[ip++];
                            goto inner_match;
                        }
                        goto first_literal_run;
                    }
                    goto outer_token;
                }
                if (ip >= ipEnd)
                {
                    throw new IOException("LZO stream ended without end marker");
                }
                t = input[ip++];
            outer_token:
                if (t >= 16)
                {
                    goto inner_match;
                }
                if (t == 0)
                {
                    for (;;)
                    {
                        if (ip >= ipEnd)
                        {
                            throw new IOException("LZO stream ended in literal length");
                        }
                        int b = input[ip++];
                        if (b != 0)
                        {
                            t += 15 + b;
                            break;
                        }
                        t += 255;
                    }
                }
                t += 3;
                if (ip + t > ipEnd || op + t > opEnd)
                {
                    throw new IOException("LZO literal run overruns input or output");
                }
                Array.Copy(input, ip, output, op, t);
                ip += t;
                op += t;

            first_literal_run:
                // After a literal run: match, or a 3-byte match at
                // M2-max-offset when the token is below 16.
                if (ip >= ipEnd)
                {
                    throw new IOException("LZO stream ended after literal run");
                }
                t = input[ip++];
                if (t >= 16)
                {
                    goto inner_match;
                }
                if (ip >= ipEnd)
                {
                    throw new IOException("LZO stream ended in short-match offset");
                }
                {
                    int b = input[ip++];
                    int mPos = op - (1 + 0x800) - (t >> 2) - (b << 2);
                    CheckLookbehind(mPos, outOffset, op, ip, inOffset);
                    if (op + 3 > opEnd)
                    {
                        throw new IOException("LZO short match overruns output");
                    }
                    output[op] = output[mPos];
                    output[op + 1] = output[mPos + 1];
                    output[op + 2] = output[mPos + 2];
                    op += 3;
                    goto match_done;
                }

            inner_match:
                {
                    int mPos;
                    int length;
                    if (t >= 64)
                    {
                        // M2 match, length 3..8.
                        if (ip >= ipEnd)
                        {
                            throw new IOException("LZO stream ended in M2 offset");
                        }
                        int b = input[ip++];
                        mPos = op - 1 - ((t >> 2) & 7) - (b << 3);
                        length = (t >> 5) + 1;
                    }
                    else if (t >= 32)
                    {
                        // M3 match, length 3+.
                        t &= 31;
                        if (t == 0)
                        {
                            for (;;)
                            {
                                if (ip >= ipEnd)
                                {
                                    throw new IOException("LZO stream ended in M3 length");
                                }
                                int b = input[ip++];
                                if (b != 0)
                                {
                                    t += 31 + b;
                                    break;
                                }
                                t += 255;
                            }
                        }
                        if (ip + 2 > ipEnd)
                        {
                            throw new IOException("LZO stream ended in M3 offset");
                        }
                        int offset = (input[ip] | (input[ip + 1] << 8)) >> 2;
                        ip += 2;
                        mPos = op - 1 - offset;
                        length = t + 2;
                    }
                    else if (t >= 16)
                    {
                        // M4 match: offset bank starts at 0x4000, bit 3 of
                        // the token selects the 0x8000 sub-bank.
                        int mOffBase = (t & 8) << 11;
                        t &= 7;
                        if (t == 0)
                        {
                            for (;;)
                            {
                                if (ip >= ipEnd)
                                {
                                    throw new IOException("LZO stream ended in M4 length");
                                }
                                int b = input[ip++];
                                if (b != 0)
                                {
                                    t += 7 + b;
                                    break;
                                }
                                t += 255;
                            }
                        }
                        if (ip + 2 > ipEnd)
                        {
                            throw new IOException("LZO stream ended in M4 offset");
                        }
                        int raw = input[ip] | (input[ip + 1] << 8);
                        ip += 2;
                        mPos = op - mOffBase - (raw >> 2);
                        if (mPos == op)
                        {
                            // End-of-stream marker (zero offset, emitted by
                            // the reference compressor as 11 00 00).
                            if (ip != ipEnd)
                            {
                                throw new IOException("LZO trailing bytes after end marker");
                            }
                            return op - outOffset;
                        }
                        mPos -= 0x4000;
                        length = t + 2;
                    }
                    else
                    {
                        // M1 match, length 2. Only valid in inner
                        // (post-match) position, including t == 0.
                        if (ip >= ipEnd)
                        {
                            throw new IOException("LZO stream ended in M1 offset");
                        }
                        int b = input[ip++];
                        mPos = op - 1 - (t >> 2) - (b << 2);
                        length = 2;
                    }

                    CheckLookbehind(mPos, outOffset, op, ip, inOffset);
                    if (op + length > opEnd)
                    {
                        throw new IOException("LZO match overruns output");
                    }
                    for (int i = 0; i < length; i++)
                    {
                        output[op + i] = output[mPos + i];
                    }
                    op += length;
                }

            match_done:
                {
                    // Trailing literal count rides in the low 2 bits of the
                    // last consumed byte (token for M1/M2, offset high byte
                    // for M3/M4). Zero means back to outer position.
                    int trailing = input[ip - 2] & 3;
                    if (trailing == 0)
                    {
                        goto outer_next;
                    }
                    if (ip + trailing > ipEnd || op + trailing > opEnd)
                    {
                        throw new IOException("LZO trailing literals overrun input or output");
                    }
                    Array.Copy(input, ip, output, op, trailing);
                    ip += trailing;
                    op += trailing;
                    if (ip >= ipEnd)
                    {
                        throw new IOException("LZO stream ended after trailing literals");
                    }
                    t = input[ip++];
                    goto inner_match;
                }
            }
        }

        private static void CheckLookbehind(int mPos, int outOffset, int op, int ip, int inOffset)
        {
            if (mPos < outOffset || mPos >= op)
            {
                throw new IOException("LZO lookbehind overrun at ip=" + (ip - inOffset) + " op=" + (op - outOffset) + " mPos=" + (mPos - outOffset));
            }
        }
    }
}
