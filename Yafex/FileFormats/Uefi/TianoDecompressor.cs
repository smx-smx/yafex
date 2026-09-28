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

namespace Yafex.FileFormats.Uefi;

/// <summary>
/// Decompressor for the Tiano/EFI custom LZ77+Huffman stream used by
/// UEFI <c>EFI_SECTION_COMPRESSION</c> sections (compression type 1).
/// The wire format is defined by the UEFI PI specification: an 8-byte
/// <c>CompSize</c>/<c>OrigSize</c> header, then 16-bit-sized blocks each
/// carrying three Huffman code sets (extra, literal/length, position).
/// Fails closed with <see cref="InvalidDataException"/> on any overrun,
/// bad table or backward-reference error.
/// </summary>
public static class TianoDecompressor
{
    private const int BitBufSize = 32;
    private const int Threshold = 3;
    private const int CodeBit = 16;
    // literal/length alphabet: 256 literals + (256 - Threshold + 2) lengths
    private const int NumLitLen = 0xFF + 256 + 2 - Threshold;
    private const int LitLenTableBits = 12;
    private const int PosTableBits = 8;
    private const int NumExtra = CodeBit + 3;
    private const int MaxPosSymbols = (1 << 5) - 1;
    private const int NumPosTree = CodeBit + 3 > MaxPosSymbols ? CodeBit + 3 : MaxPosSymbols;

    /// <summary>Tiano variant (section compression type 1).</summary>
    public const int TianoPositionBits = 5;

    /// <summary>Legacy EFI variant.</summary>
    public const int EfiPositionBits = 4;

    /// <summary>
    /// Decompresses a <c>EFI_SECTION_COMPRESSION</c> payload. Compression
    /// type 1 identifies the EFI and Tiano variants with the same byte, so
    /// EFI is tried first and Tiano second (the order used across the
    /// ecosystem); the stream's own <c>OrigSize</c> validates the winner.
    /// </summary>
    public static byte[] DecompressSection(byte[] src, int expectedSize)
    {
        InvalidDataException? first = null;
        foreach (int bits in new[] { EfiPositionBits, TianoPositionBits })
        {
            try
            {
                var out_ = Decompress(src, bits);
                if (out_.Length == expectedSize)
                {
                    return out_;
                }
            }
            catch (InvalidDataException ex)
            {
                first ??= ex;
            }
        }
        throw new InvalidDataException(
            "neither EFI nor Tiano variant decoded the section"
            + (first != null ? ": " + first.Message : ""));
    }

    public static byte[] Decompress(byte[] src, int positionBits = TianoPositionBits)
    {
        if (src == null || src.Length < 8)
        {
            throw new InvalidDataException("truncated Tiano header");
        }
        int compSize = ReadU32(src, 0);
        int origSize = ReadU32(src, 4);
        if (origSize < 0 || origSize > 1 << 30)
        {
            throw new InvalidDataException("implausible Tiano output size");
        }
        if (compSize < 0 || compSize + 8 > src.Length)
        {
            throw new InvalidDataException("Tiano CompSize overruns input");
        }
        return new Decoder(src, 8, compSize, origSize, positionBits).Run();
    }

    private static int ReadU32(byte[] buf, int off)
    {
        return buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24);
    }

    /// <summary>Per-stream decode state (single use).</summary>
    private sealed class Decoder
    {
        private readonly byte[] _src;
        private int _srcPos;
        private int _srcLeft;
        private readonly byte[] _dst;
        private int _dstPos;
        private readonly int _positionBits;

        private uint _bitBuf;
        private uint _subBitBuf;
        private int _bitCount;
        private int _blockLeft;
        private string? _badStage;

        private readonly byte[] _litLen = new byte[NumLitLen];
        private readonly byte[] _posLen = new byte[NumPosTree];
        private readonly ushort[] _litTable = new ushort[1 << LitLenTableBits];
        private readonly ushort[] _posTable = new ushort[1 << PosTableBits];
        private readonly ushort[] _litLeft = new ushort[2 * NumLitLen];
        private readonly ushort[] _litRight = new ushort[2 * NumLitLen];
        private readonly ushort[] _posLeft = new ushort[2 * NumPosTree];
        private readonly ushort[] _posRight = new ushort[2 * NumPosTree];

        public Decoder(byte[] src, int off, int compSize, int origSize, int positionBits)
        {
            _src = src;
            _srcPos = off;
            _srcLeft = compSize;
            _dst = new byte[origSize];
            _positionBits = positionBits;
        }

        public byte[] Run()
        {
            Fill(BitBufSize);
            for (;;)
            {
                int sym = DecodeLitLen();
                if (_badStage != null)
                {
                    throw new InvalidDataException("bad Tiano stage " + _badStage);
                }
                if (sym < 256)
                {
                    _dst[_dstPos++] = (byte)sym;
                    if (_dstPos >= _dst.Length)
                    {
                        return _dst;
                    }
                }
                else
                {
                    int length = sym - (0xFF + 1 - Threshold);
                    int distance = DecodePosition() + 1;
                    int copyFrom = _dstPos - distance;
                    if (copyFrom < 0)
                    {
                        throw new InvalidDataException("Tiano backward reference");
                    }
                    for (int i = 0; i < length; i++)
                    {
                        if (_dstPos >= _dst.Length)
                        {
                            return _dst;
                        }
                        if (copyFrom >= _dst.Length)
                        {
                            throw new InvalidDataException("Tiano match overruns output");
                        }
                        _dst[_dstPos++] = _dst[copyFrom++];
                    }
                    if (_dstPos >= _dst.Length)
                    {
                        return _dst;
                    }
                }
            }
        }

        private void Fill(int bits)
        {
            _bitBuf <<= bits;
            while (bits > _bitCount)
            {
                _bitBuf |= _subBitBuf << (bits - _bitCount);
                bits -= _bitCount;
                if (_srcLeft > 0)
                {
                    _srcLeft--;
                    _subBitBuf = _src[_srcPos++];
                    _bitCount = 8;
                }
                else
                {
                    _subBitBuf = 0;
                    _bitCount = 8;
                }
            }
            _bitCount -= bits;
            _bitBuf |= _subBitBuf >> _bitCount;
        }

        private uint Take(int bits)
        {
            uint outBits = bits == 0 ? 0 : _bitBuf >> (BitBufSize - bits);
            Fill(bits);
            return outBits;
        }

        // Builds the direct prefix table plus overflow trees. Table slots
        // below NumSymbols hold symbols (leaves); slots at/above hold node
        // indices into left/right. Returns false on a corrupt description.
        private static bool BuildTable(int symbols, byte[] lengths, int tableBits,
            ushort[] table, ushort[] left, ushort[] right)
        {
            var count = new ushort[17];
            for (int i = 0; i < symbols; i++)
            {
                if (lengths[i] > 16)
                {
                    return false;
                }
                count[lengths[i]]++;
            }
            var start = new ushort[18];
            for (int i = 1; i <= 16; i++)
            {
                start[i + 1] = (ushort)(start[i] + (count[i] << (16 - i)));
            }
            if (start[17] != 0)
            {
                return false;
            }
            int shift = 16 - tableBits;
            var weight = new ushort[17];
            for (int i = 1; i <= tableBits; i++)
            {
                start[i] >>= shift;
                weight[i] = (ushort)(1 << (tableBits - i));
            }
            for (int i = tableBits + 1; i <= 16; i++)
            {
                weight[i] = (ushort)(1 << (16 - i));
            }
            int first = start[tableBits + 1] >> shift;
            if (first != 0)
            {
                for (int i = first, n = 1 << tableBits; i < n; i++)
                {
                    table[i] = 0;
                }
            }
            int avail = symbols;
            uint mask = 1u << (15 - tableBits);
            int maxLen = 1 << tableBits;
            for (int ch = 0; ch < symbols; ch++)
            {
                int len = lengths[ch];
                if (len == 0 || len >= 17)
                {
                    continue;
                }
                int next = start[len] + weight[len];
                if (len <= tableBits)
                {
                    if (start[len] >= next || next > maxLen)
                    {
                        return false;
                    }
                    for (int i = start[len]; i < next; i++)
                    {
                        table[i] = (ushort)ch;
                    }
                }
                else
                {
                    int code = start[len];
                    int slot = code >> shift;
                    int depth = len - tableBits;
                    int node = table[slot];
                    if (node == 0)
                    {
                        if (avail >= left.Length)
                        {
                            return false;
                        }
                        node = avail++;
                        table[slot] = (ushort)node;
                    }
                    int c = code;
                    for (int d = depth; d > 1; d--)
                    {
                        bool bit = (c & mask) != 0;
                        int child = bit ? right[node] : left[node];
                        if (child == 0)
                        {
                            if (avail >= left.Length)
                            {
                                return false;
                            }
                            child = avail++;
                            if (bit)
                            {
                                right[node] = (ushort)child;
                            }
                            else
                            {
                                left[node] = (ushort)child;
                            }
                        }
                        node = child;
                        c <<= 1;
                    }
                    if (((uint)c & mask) != 0)
                    {
                        right[node] = (ushort)ch;
                    }
                    else
                    {
                        left[node] = (ushort)ch;
                    }
                }
                start[len] = (ushort)next;
            }
            return true;
        }

        private bool ReadPosLengths(int symbols, int lenBits, int special)
        {
            int count = (int)Take(lenBits);
            if (count == 0)
            {
                int single = (int)Take(lenBits);
                for (int i = 0; i < _posTable.Length; i++)
                {
                    _posTable[i] = (ushort)single;
                }
                Array.Clear(_posLen, 0, _posLen.Length);
                return true;
            }
            int idx = 0;
            while (idx < count && idx < NumPosTree)
            {
                int c = (int)(_bitBuf >> (BitBufSize - 3));
                if (c == 7)
                {
                    uint mask = 1u << (BitBufSize - 1 - 3);
                    while ((mask & _bitBuf) != 0)
                    {
                        mask >>= 1;
                        c++;
                    }
                }
                Fill(c < 7 ? 3 : c - 3);
                _posLen[idx++] = (byte)c;
                if (idx == special)
                {
                    int skip = (int)Take(2);
                    for (int k = 0; k < skip && idx < NumPosTree; k++)
                    {
                        _posLen[idx++] = 0;
                    }
                }
            }
            while (idx < symbols && idx < NumPosTree)
            {
                _posLen[idx++] = 0;
            }
            return BuildTable(symbols, _posLen, PosTableBits, _posTable, _posLeft, _posRight);
        }

        private bool ReadLitLengths()
        {
            int count = (int)Take(9);
            if (count == 0)
            {
                int single = (int)Take(9);
                Array.Clear(_litLen, 0, _litLen.Length);
                for (int i = 0; i < _litTable.Length; i++)
                {
                    _litTable[i] = (ushort)single;
                }
                return true;
            }
            int idx = 0;
            while (idx < count)
            {
                int c = _posTable[_bitBuf >> (BitBufSize - 8)];
                if (c >= NumExtra)
                {
                    uint mask = 1u << (BitBufSize - 1 - 8);
                    do
                    {
                        c = (_bitBuf & mask) != 0 ? _posRight[c] : _posLeft[c];
                        if (c >= _posLeft.Length)
                        {
                            return false;
                        }
                        mask >>= 1;
                    } while (c >= NumExtra);
                }
                Fill(_posLen[c]);
                if (c <= 2)
                {
                    int run = c == 0 ? 1 : c == 1 ? (int)Take(4) + 3 : (int)Take(9) + 20;
                    for (int k = 0; k < run; k++)
                    {
                        if (idx >= NumLitLen)
                        {
                            return false;
                        }
                        _litLen[idx++] = 0;
                    }
                }
                else
                {
                    if (idx >= NumLitLen)
                    {
                        return false;
                    }
                    _litLen[idx++] = (byte)(c - 2);
                }
            }
            while (idx < NumLitLen)
            {
                _litLen[idx++] = 0;
            }
            return BuildTable(NumLitLen, _litLen, LitLenTableBits, _litTable, _litLeft, _litRight);
        }

        private int DecodeLitLen()
        {
            if (_blockLeft == 0)
            {
                _blockLeft = (int)Take(16);
                if (!ReadPosLengths(NumExtra, 5, 3))
                {
                    _badStage = "extra";
                    return 0;
                }
                if (!ReadLitLengths())
                {
                    _badStage = "litlen";
                    return 0;
                }
                if (!ReadPosLengths(MaxPosSymbols, _positionBits, -1))
                {
                    _badStage = "poslen";
                    return 0;
                }
            }
            _blockLeft--;
            int sym = _litTable[_bitBuf >> (BitBufSize - LitLenTableBits)];
            if (sym >= NumLitLen)
            {
                uint mask = 1u << (BitBufSize - 1 - LitLenTableBits);
                do
                {
                    sym = (_bitBuf & mask) != 0 ? _litRight[sym] : _litLeft[sym];
                    if (sym >= _litLeft.Length)
                    {
                        _badStage = "litwalk";
                        return 0;
                    }
                    mask >>= 1;
                } while (sym >= NumLitLen);
            }
            Fill(_litLen[sym]);
            return sym;
        }

        private int DecodePosition()
        {
            int val = _posTable[_bitBuf >> (BitBufSize - 8)];
            if (val >= MaxPosSymbols)
            {
                uint mask = 1u << (BitBufSize - 1 - 8);
                do
                {
                    val = (_bitBuf & mask) != 0 ? _posRight[val] : _posLeft[val];
                    if (val >= _posLeft.Length)
                    {
                        throw new InvalidDataException("bad Tiano position tree");
                    }
                    mask >>= 1;
                } while (val >= MaxPosSymbols);
            }
            Fill(_posLen[val]);
            if (val > 1)
            {
                val = (1 << (val - 1)) + (int)Take(val - 1);
            }
            return val;
        }
    }
}
