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
/// LZMA (7z) range-coder decoder for UEFI <c>GUID_DEFINED</c> sections
/// carrying the LZMA definition GUID. Wire framing is the 13-byte Alone
/// header (5 property bytes + 8-byte LE output size) described by the
/// UEFI PI specification; the bit-level algorithm (range coder, literal /
/// length / distance code sets, 12-state machine) follows the public LZMA
/// description. Like the firmware ecosystem decoders, clean input
/// exhaustion ends the stream (some images understate... understate the
/// trailing bytes); every other anomaly fails closed with
/// <see cref="InvalidDataException"/>. Callers must validate content
/// (FV parse / section walk) before trusting the bytes.
/// </summary>
public static class LzmaDecoder
{
    public const int AloneHeaderSize = 13;
    public const int PropsSize = 5;

    private const int NumBitModelTotalBits = 11;
    private const int BitModelTotal = 1 << NumBitModelTotalBits;
    private const int NumMoveBits = 5;
    private const uint TopValue = 1u << 24;
    private const uint BadRepCode = 0xC0000000u - 0x400u;

    private const int NumPosBitsMax = 4;
    private const int LenNumLowBits = 3;
    private const int LenNumLowSymbols = 1 << LenNumLowBits;
    private const int LenNumHighBits = 8;
    private const int LenNumHighSymbols = 1 << LenNumHighBits;
    private const int LenNumProbs = 2 * (16 << LenNumLowBits) + LenNumHighSymbols;

    private const int NumStates = 12;
    private const int NumLitStates = 7;
    private const int StartPosModel = 4;
    private const int EndPosModel = 14;
    private const int NumFullDistances = 1 << (EndPosModel >> 1);
    private const int NumPosSlotBits = 6;
    private const int NumLenToPosStates = 4;
    private const int NumAlignBits = 4;
    private const int MatchMinLen = 2;
    private const int MatchSpecLenStart = MatchMinLen + LenNumLowSymbols * 2 + LenNumHighSymbols;

    // Probability table layout (absolute indices into one array; the
    // SpecPos reverse tree lives at 0..127, everything else at 128+).
    private const int Base = 1664;
    private const int PIsRep0Long = Base - 1664 + NumFullDistances;
    private const int PRepLen = PIsRep0Long + 256;
    private const int PLen = PRepLen + LenNumProbs;
    private const int PIsMatch = PLen + LenNumProbs;
    private const int PAlign = PIsMatch + 256;
    private const int PIsRep = PAlign + 16;
    private const int PIsRepG0 = PIsRep + NumStates;
    private const int PIsRepG1 = PIsRepG0 + NumStates;
    private const int PIsRepG2 = PIsRepG1 + NumStates;
    private const int PPosSlot = PIsRepG2 + NumStates;
    private const int PLiteral = PPosSlot + (NumLenToPosStates << NumPosSlotBits);
    private const int NumBaseProbs = PLiteral;
    private const int LitSize = 0x300;

    /// <summary>Maximum accepted declared output (hostile-size guard).</summary>
    public const long MaxOutput = 1L << 29;


    public static byte[] Decompress(byte[] src)
    {
        if (src == null || src.Length < AloneHeaderSize)
        {
            throw new InvalidDataException("truncated LZMA header");
        }
        int lc, lp, pb, dicSize;
        DecodeProps(src, out lc, out lp, out pb, out dicSize);
        long size = 0;
        for (int i = 12; i >= 5; i--)
        {
            size = (size << 8) + src[i];
        }
        if (size == -1)
        {
            // unknown size: end-marked stream, grow on demand
            return new Runner(src, AloneHeaderSize, src.Length - AloneHeaderSize,
                lc, lp, pb, dicSize, -1).Run();
        }
        if (size < 0 || size > MaxOutput)
        {
            throw new InvalidDataException("implausible LZMA output size");
        }
        return new Runner(src, AloneHeaderSize, src.Length - AloneHeaderSize,
            lc, lp, pb, dicSize, (int)size).Run();
    }

    private static void DecodeProps(byte[] src, out int lc, out int lp, out int pb, out int dicSize)
    {
        int d = src[0];
        if (d >= 9 * 5 * 5)
        {
            throw new InvalidDataException("unsupported LZMA properties");
        }
        dicSize = src[1] | (src[2] << 8) | (src[3] << 16) | (src[4] << 24);
        if (dicSize < 1 << 12)
        {
            dicSize = 1 << 12;
        }
        lc = d % 9;
        d /= 9;
        pb = d / 5;
        lp = d % 5;
    }

    private sealed class Runner
    {
        private readonly byte[] _src;
        private int _pos;
        private readonly int _end;
        private byte[] _out;
        private int _outPos;
        private readonly bool _growable;
        private readonly int _lc, _lp, _pb, _dicSize;
        private readonly ushort[] _probs;
        private uint _range, _code;
        private uint _processedPos;
        private uint _checkDicSize;
        // rep distances are 1-based (1 = previous byte); init to 1
        private uint _rep0 = 1, _rep1 = 1, _rep2 = 1, _rep3 = 1;
        private uint _state;
        private readonly uint _pbMask;
        private readonly uint _lpMask;

        public Runner(byte[] src, int off, int len, int lc, int lp, int pb, int dicSize, int outSize)
        {
            _src = src;
            _pos = off;
            _end = off + len;
            _growable = outSize < 0;
            _out = new byte[_growable ? 1 << 20 : outSize];
            _lc = lc;
            _lp = lp;
            _pb = pb;
            _dicSize = dicSize;
            _probs = new ushort[NumBaseProbs + (LitSize << (lc + lp))];
            for (int i = 0; i < _probs.Length; i++)
            {
                _probs[i] = BitModelTotal >> 1;
            }
            _pbMask = (uint)((1 << pb) - 1);
            _lpMask = (uint)((0x100 << lp) - (0x100 >> lc));
        }

        public byte[] Run()
        {
            if (!_growable && _out.Length == 0)
            {
                return _out;
            }
            if (_pos + 5 > _end || _src[_pos] != 0)
            {
                throw new InvalidDataException("bad LZMA range init");
            }
            _code = ((uint)_src[_pos + 1] << 24) | ((uint)_src[_pos + 2] << 16)
                | ((uint)_src[_pos + 3] << 8) | _src[_pos + 4];
            _pos += 5;
            _range = 0xFFFFFFFFu;
            if (_checkDicSize == 0 && _processedPos == 0 && _code >= BadRepCode)
            {
                throw new InvalidDataException("bad LZMA stream");
            }
            while (EnsureRoom(1))
            {
                if (_pos >= _end)
                {
                    // clean input end (ecosystem behavior for short tails)
                    break;
                }
                uint posState = (_processedPos & _pbMask) << 4;
                if (DecodeBit(PIsMatch + (int)(posState + _state)) == 0)
                {
                    DecodeLiteral(posState);
                }
                else
                {
                    if (!DecodeMatch(posState))
                    {
                        break; // end marker
                    }
                }
            }
            if (!_growable && _outPos == _out.Length)
            {
                return _out;
            }
            var trunc = new byte[_outPos];
            Buffer.BlockCopy(_out, 0, trunc, 0, trunc.Length);
            return trunc;
        }

        private bool EnsureRoom(long need)
        {
            if (_outPos + need <= _out.Length)
            {
                return true;
            }
            if (!_growable)
            {
                return false;
            }
            long ns = Math.Max((long)_out.Length * 2, _outPos + need);
            if (ns > MaxOutput)
            {
                throw new InvalidDataException("LZMA output exceeds cap");
            }
            Array.Resize(ref _out, (int)ns);
            return true;
        }

        private uint Take()
        {
            if (_pos >= _end)
            {
                throw new InvalidDataException("truncated LZMA stream");
            }
            return _src[_pos++];
        }

        private uint DecodeBit(int probIdx)
        {
            ushort prob = _probs[probIdx];
            if (_range < TopValue)
            {
                _range <<= 8;
                _code = (_code << 8) | Take();
            }
            uint bound = (_range >> NumBitModelTotalBits) * prob;
            if (_code < bound)
            {
                _range = bound;
                _probs[probIdx] = (ushort)(prob + ((BitModelTotal - prob) >> NumMoveBits));
                return 0;
            }
            _range -= bound;
            _code -= bound;
            _probs[probIdx] = (ushort)(prob - (prob >> NumMoveBits));
            return 1;
        }

        private uint DecodeTree(int baseIdx, int limit)
        {
            uint sym = 1;
            do
            {
                sym = (sym << 1) + DecodeBit(baseIdx + (int)sym);
            } while (sym < (uint)limit);
            return sym - (uint)limit;
        }

        // LSB-first tree with input-dependent nodes (SpecPos / Align).
        // A final half-step (the Align table's LAST row) only adds.
        private uint DecodeReverse(int baseIdx, uint start, int count, int lastAdd = 0)
        {
            uint i = start;
            uint m = 1;
            uint value = 0;
            for (int k = 0; k < count; k++)
            {
                if (DecodeBit(baseIdx + (int)i) == 0)
                {
                    i += m;
                }
                else
                {
                    value += m;
                    i += m * 2;
                }
                m *= 2;
            }
            if (lastAdd > 0 && DecodeBit(baseIdx + (int)i) != 0)
            {
                value += (uint)lastAdd;
            }
            return value;
        }

        private void DecodeLiteral(uint posState)
        {
            uint prevByte = _processedPos == 0 && _checkDicSize == 0 ? 0 : (uint)_out[_outPos - 1];
            int baseIdx = PLiteral + 3 * (int)(((((int)_processedPos << 8) + (int)prevByte) & (int)_lpMask) << _lc);
            uint symbol = 1;
            if (_state < NumLitStates)
            {
                _state -= _state < 4 ? _state : 3;
                for (int i = 0; i < 8; i++)
                {
                    symbol = (symbol << 1) + DecodeBit(baseIdx + (int)symbol);
                }
            }
            else
            {
                uint matchByte = _out[_outPos - (int)_rep0];
                uint offs = 0x100;
                _state -= _state < 10 ? 3u : 6u;
                for (int i = 0; i < 8; i++)
                {
                    matchByte <<= 1;
                    uint bit = offs;
                    offs &= matchByte;
                    uint b = DecodeBit(baseIdx + (int)(offs + bit + symbol));
                    symbol = (symbol << 1) + b;
                    if (b == 0)
                    {
                        offs ^= bit;
                    }
                }
            }
            _out[_outPos++] = (byte)symbol;
            _processedPos++;
            if (_checkDicSize == 0 && _processedPos >= (uint)_dicSize)
            {
                _checkDicSize = (uint)_dicSize;
            }
        }

        // Returns false on the end-of-payload marker.
        private bool DecodeMatch(uint posState)
        {
            if (DecodeBit(PIsRep + (int)_state) == 0)
            {
                _state += NumStates;
            }
            else
            {
                if (_checkDicSize == 0 && _processedPos == 0)
                {
                    throw new InvalidDataException("bad LZMA rep");
                }
                if (DecodeBit(PIsRepG0 + (int)_state) == 0)
                {
                    if (DecodeBit(PIsRep0Long + (int)(posState + _state)) == 0)
                    {
                        // short rep0
                        _out[_outPos] = _out[_outPos - (int)_rep0];
                        _outPos++;
                        _processedPos++;
                        _state = _state < NumLitStates ? 9u : 11u;
                        if (_checkDicSize == 0 && _processedPos >= (uint)_dicSize)
                        {
                            _checkDicSize = (uint)_dicSize;
                        }
                        return true;
                    }
                }
                else
                {
                    uint repDist;
                    if (DecodeBit(PIsRepG1 + (int)_state) == 0)
                    {
                        repDist = _rep1;
                    }
                    else
                    {
                        if (DecodeBit(PIsRepG2 + (int)_state) == 0)
                        {
                            repDist = _rep2;
                        }
                        else
                        {
                            repDist = _rep3;
                            _rep3 = _rep2;
                        }
                        _rep2 = _rep1;
                    }
                    _rep1 = _rep0;
                    _rep0 = repDist;
                }
                _state = _state < NumLitStates ? 8u : 11u;
                uint repLen = DecodeLength(PRepLen, posState);
                CopyMatch(repLen + MatchMinLen);
                return true;
            }
            uint len = DecodeLength(PLen, posState);
            uint distance = DecodeDistance(len, out bool endMarker);
            if (endMarker)
            {
                return false;
            }
            _rep3 = _rep2;
            _rep2 = _rep1;
            _rep1 = _rep0;
            _rep0 = distance + 1;
            _state = _state < NumStates + NumLitStates ? (uint)NumLitStates : (uint)(NumLitStates + 3);
            uint limit = _checkDicSize == 0 ? _processedPos : _checkDicSize;
            if (distance >= limit)
            {
                throw new InvalidDataException("bad LZMA distance");
            }
            CopyMatch(len + MatchMinLen);
            return true;
        }

        private uint DecodeLength(int coder, uint posState)
        {
            if (DecodeBit(coder) == 0)
            {
                return DecodeTree(coder + (int)posState, LenNumLowSymbols);
            }
            if (DecodeBit(coder + 8) == 0)
            {
                return LenNumLowSymbols + DecodeTree(coder + 128 + (int)posState, LenNumLowSymbols);
            }
            return LenNumLowSymbols * 2 + DecodeTree(coder + 256, LenNumHighSymbols);
        }

        private uint DecodeDistance(uint len, out bool endMarker)
        {
            endMarker = false;
            uint lenState = len < NumLenToPosStates ? len : NumLenToPosStates - 1;
            uint posSlot = DecodeTree(PPosSlot + (int)(lenState << NumPosSlotBits), 1 << NumPosSlotBits);
            if (posSlot < StartPosModel)
            {
                return posSlot;
            }
            uint numDirect = (posSlot >> 1) - 1;
            uint distance = 2 | (posSlot & 1);
            if (posSlot < EndPosModel)
            {
                distance <<= (int)numDirect;
                distance += DecodeReverse(0, distance + 1, (int)numDirect);
            }
            else
            {
                numDirect -= NumAlignBits;
                while (numDirect-- > 0)
                {
                    if (_range < TopValue)
                    {
                        _range <<= 8;
                        _code = (_code << 8) | Take();
                    }
                    _range >>= 1;
                    _code -= _range;
                    uint bit = (_code >> 31) == 0 ? 1u : 0u;
                    if (bit == 0)
                    {
                        _code += _range;
                    }
                    distance = (distance << 1) + bit;
                }
                distance <<= NumAlignBits;
                distance += DecodeReverse(PAlign, 1, 3, lastAdd: 8);
                if (distance == 0xFFFFFFFFu)
                {
                    endMarker = true;
                    return 0;
                }
            }
            return distance;
        }

        private void CopyMatch(uint len)
        {
            int from = _outPos - (int)_rep0;
            if (from < 0)
            {
                throw new InvalidDataException("bad LZMA match");
            }
            long room = _out.Length - _outPos;
            long take = Math.Min(len, room);
            if (take < len && _growable)
            {
                EnsureRoom(len);
                take = len;
            }
            for (long i = 0; i < take; i++)
            {
                _out[_outPos++] = _out[from++];
            }
            _processedPos += (uint)take;
            if (_checkDicSize == 0 && _processedPos >= (uint)_dicSize)
            {
                _checkDicSize = (uint)_dicSize;
            }
        }
    }
}
