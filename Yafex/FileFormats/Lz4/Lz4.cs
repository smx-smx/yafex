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

namespace Yafex.FileFormats.Lz4;

/// <summary>
/// Raw LZ4 block decompressor (literal/match sequences per the block format
/// spec). Bounds-checked: any overrun, zero offset or backward reference
/// fails closed with <see cref="InvalidDataException"/>.
/// </summary>
public static class Lz4
{
    private const int MinMatch = 4;

    public static int DecompressBlock(byte[] src, int srcOff, int srcLen, byte[] dst, int dstOff, int dstMax)
    {
        return DecompressBlock(src, srcOff, srcLen, dst, dstOff, dstMax, out _);
    }

    public static int DecompressBlock(byte[] src, int srcOff, int srcLen, byte[] dst, int dstOff, int dstMax, out int consumed)
    {
        int ip = srcOff;
        int srcEnd = srcOff + srcLen;
        int op = dstOff;
        int dstEnd = dstOff + dstMax;

        while (ip < srcEnd)
        {
            int token = src[ip++];
            int litLen = token >> 4;
            if (litLen == 15)
            {
                int l;
                do
                {
                    if (ip >= srcEnd) throw new InvalidDataException("truncated literal length");
                    l = src[ip++];
                    litLen += l;
                } while (l == 255);
            }
            if (ip + litLen > srcEnd || op + litLen > dstEnd)
            {
                throw new InvalidDataException("literal overrun");
            }
            Buffer.BlockCopy(src, ip, dst, op, litLen);
            ip += litLen;
            op += litLen;
            if (ip >= srcEnd)
            {
                break;
            }
            if (ip + 2 > srcEnd)
            {
                throw new InvalidDataException("truncated match offset");
            }
            int offset = src[ip] | (src[ip + 1] << 8);
            ip += 2;
            int produced = op - dstOff;
            if (offset < 1 || offset > produced)
            {
                throw new InvalidDataException("invalid match offset");
            }
            int matchLen = (token & 0x0F) + MinMatch;
            if (matchLen == MinMatch + 15)
            {
                int l;
                do
                {
                    if (ip >= srcEnd) throw new InvalidDataException("truncated match length");
                    l = src[ip++];
                    matchLen += l;
                } while (l == 255);
            }
            if (op + matchLen > dstEnd)
            {
                throw new InvalidDataException("match overrun");
            }
            // overlapping copy, byte by byte (offset < matchLen is legal)
            int refPos = op - offset;
            for (int i = 0; i < matchLen; i++)
            {
                dst[op++] = dst[refPos++];
            }
        }
        consumed = ip - srcOff;
        return op - dstOff;
    }
}
