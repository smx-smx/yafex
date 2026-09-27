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
using System;
using System.IO;

namespace Yafex.FileFormats.Lzo;

/// <summary>
/// Raw LZO1X (v0, no RLE) block decoder.
/// </summary>
/// <remarks>
/// Byte-level format (behaves like the Linux kernel's lzo1x_decompress_safe):
/// a stream of opcode bytes with inline operands. Lengths use a variable-size
/// code: a few bits inline, plus 255 per zero byte and a final nonzero byte
/// when the inline bits are all zero. After every match except the long
/// literal run, 0-3 literal bytes ("state") follow the copied block.
///
/// Opcodes (b = opcode byte):
/// - first byte only: 0..15 regular instruction; 16 invalid (empty history);
///   17 version marker (v1 only, rejected here); 18..21 copy b-17 literals
///   with state b-17; 22..255 copy b-17 literals with state 4.
/// - 0..15: depends on incoming state — state 0: long literal run
///   (len 3+L, state 4); state 1..3: 2-byte match within 1kB
///   (dist (H&lt;&lt;2)+(b&gt;&gt;2&amp;3)+1, then S literals); state 4: 3-byte
///   match at 2..3kB (dist (H&lt;&lt;2)+((b&gt;&gt;2)&amp;3)+2049, then S literals).
/// - 16..31: match within 16..48kB, len 2+L, LE16 distance operand,
///   dist 16384+(H&lt;&lt;14)+D; dist 16384 ends the stream.
/// - 32..63: short match within 16kB, len 2+L, dist D+1.
/// - 64..127: 3-4 byte match within 2kB, len 3+L.
/// - 128..255: 5-8 byte match within 2kB, len 5+L.
///
/// All copies are bounds-checked; any overrun, backward reference or
/// size mismatch throws <see cref="InvalidDataException"/>.
/// </remarks>
public static class Lzo1X
{
    public static byte[] Decompress(byte[] input, int outputLength)
    {
        if (outputLength < 0)
            throw new ArgumentOutOfRangeException(nameof(outputLength));
        var output = new byte[outputLength];
        int ip = 0;
        int op = 0;
        int In() => ip < input.Length ? input[ip++] : throw new InvalidDataException("truncated LZO stream");

        void Literals(int count)
        {
            if (count < 0 || op + count > output.Length || ip + count > input.Length)
                throw new InvalidDataException("LZO literal run out of range");
            Array.Copy(input, ip, output, op, count);
            ip += count;
            op += count;
        }

        void Match(int distance, int length, int state)
        {
            if (distance < 1 || distance > op)
                throw new InvalidDataException("LZO invalid match distance");
            if (length < 0 || op + length > output.Length)
                throw new InvalidDataException("LZO match out of range");
            // forward sliding copy: handles distance < length (repeating pattern)
            for (int i = 0; i < length; i++)
            {
                output[op] = output[op - distance];
                op++;
            }
        }

        int ExtendedLength(int inlineBits, int inlineMax, int bias)
        {
            if (inlineBits != 0) return bias + inlineBits;
            int length = inlineMax;
            while (true)
            {
                int b = In();
                if (b != 0) return bias + length + b;
                length += 255;
            }
        }

        // ---- first byte ----
        // Mirrors the canonical decoder: a leading 17 is a bitstream-version
        // word (v1/RLE only; skipped, RLE sequences are rejected below by the
        // distance/length checks), 18..21 copy b-17 literals with state b-17,
        // 22..255 copy b-17 literals with state 4.
        int state;
        {
            if (input.Length >= 5 && input[0] == 17)
            {
                ip += 2;
                state = 0;
            }
            else
            {
                int b = In();
                if (b <= 15)
                {
                    state = 0;
                    ip--; // reprocess as a regular instruction below
                }
                else if (b == 17)
                {
                    throw new InvalidDataException("invalid first LZO opcode 17");
                }
                else
                {
                    int run = b - 17;
                    Literals(run);
                    state = b <= 21 ? run : 4;
                }
            }
        }

        // ---- main loop ----
        while (true)
        {
            int b = In();
            if (b < 16)
            {
                if (state == 0)
                {
                    int len = ExtendedLength(b, 15, 3);
                    state = 4;
                    Literals(len);
                }
                else if (state < 4)
                {
                    int s = b & 3;
                    int h = In();
                    int dist = (h << 2) + ((b >> 2) & 3) + 1;
                    Match(dist, 2, s);
                    state = s;
                    Literals(s);
                }
                else
                {
                    int s = b & 3;
                    int h = In();
                    int dist = (h << 2) + ((b >> 2) & 3) + 2049;
                    Match(dist, 3, s);
                    state = s;
                    Literals(s);
                }
            }
            else if (b < 32)
            {
                int len = ExtendedLength(b & 7, 7, 2);
                int s = In();
                int d = In();
                int dist = 16384 + ((b & 8) << 11) + (((d << 8) | s) >> 2);
                if (dist == 16384)
                {
                    // end of stream: only the short form (length 3) is valid
                    if (len != 3 || op != output.Length || ip != input.Length)
                        throw new InvalidDataException("LZO end marker at wrong position");
                    return output;
                }
                state = s & 3;
                Match(dist, len, state);
                Literals(state);
            }
            else if (b < 64)
            {
                int len = ExtendedLength(b & 31, 31, 2);
                int s = In();
                int d = In();
                int dist = (((d << 8) | s) >> 2) + 1;
                state = s & 3;
                Match(dist, len, state);
                Literals(state);
            }
            else if (b < 128)
            {
                int len = 3 + ((b >> 5) & 1);
                int h = In();
                int dist = (h << 3) + ((b >> 2) & 7) + 1;
                state = b & 3;
                Match(dist, len, state);
                Literals(state);
            }
            else
            {
                int len = 5 + ((b >> 5) & 3);
                int h = In();
                int dist = (h << 3) + ((b >> 2) & 7) + 1;
                state = b & 3;
                Match(dist, len, state);
                Literals(state);
            }
        }
    }
}
