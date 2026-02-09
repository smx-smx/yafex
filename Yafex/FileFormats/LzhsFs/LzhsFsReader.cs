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
using System.Runtime.CompilerServices;

using Smx.SharpIO.Memory.Buffers;

using Yafex.FileFormats.Lzhs;

namespace Yafex.FileFormats.LzhsFs
{
    public class LzhsChunk
    {
        public readonly LzhsHeader SegmentHeader;
        public readonly LzhsHeader Header;
        public readonly ReadOnlyMemory64<byte> SegmentData;
        public readonly long InputOffset;
        public readonly long OutputOffset;

        private static readonly int HEADER_SIZE = Unsafe.SizeOf<LzhsHeader>();

        private uint Pad(uint num, uint align = 16) {
            uint rem = num % align;
            uint pad = rem switch {
                0 => 0,
                _ => 16 - rem
            };
            return num + pad; 
        }

        public long SizeCompressed => Header.CompressedSize;
        public long SizeUncompressed => SegmentHeader.UncompressedSize;

        public ushort Checksum => Header.Checksum;

        public bool IsUncompressed =>
            Header.CompressedSize == Header.UncompressedSize &&
            Header.Checksum == 0x00;

        public bool IsZeroFill => 
            Header.CompressedSize == 0 && Header.UncompressedSize > 0 &&
            Header.Checksum == 0x00;

        public long SegmentIndex => SegmentHeader.Checksum;
        public bool IsCompressed => !IsUncompressed;
        
        /// <summary>
        /// Size of the inner LZHS + the outer lzhs header
        /// </summary>
        public long SegmentSize => HEADER_SIZE + Pad(SegmentHeader.CompressedSize);

        public LzhsChunk(ReadOnlyMemory64<byte> data, long offsetIn, long offsetOut)
        {
            InputOffset = offsetIn;
            OutputOffset = offsetOut;

            var pos = offsetIn;
            SegmentHeader = new LzhsHeader(data.Slice(pos, HEADER_SIZE).Span);
            pos += HEADER_SIZE;

            Header = new LzhsHeader(data.Slice(pos, HEADER_SIZE).Span);

            SegmentData = IsZeroFill
                ? new byte[SegmentHeader.UncompressedSize]
                : data.Slice(pos, SegmentSize - HEADER_SIZE);
        }

        public LzhsDecoder? NewDecoder()
        {
            return new LzhsDecoder(SegmentData);
        }
    }
}
