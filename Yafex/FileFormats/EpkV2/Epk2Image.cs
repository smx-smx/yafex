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
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using Smx.SharpIO.Memory.Buffers;

using Yafex.FileFormats.Epk;
using Yafex.Support;

namespace Yafex.FileFormats.EpkV2
{
    /// <summary>
    /// A PAK inside an EPK2 image, described lazily through its segment map.
    /// </summary>
    public class Epk2PakImage
    {
        public PAK_V2_HEADER Header;
        public int SegmentCount;
        public PakImage Image = null!;
    }

    /// <summary>
    /// Walks the EPK2 segment layout without materializing any payload, producing
    /// <see cref="PakImage"/> descriptors usable both by the standalone extractor
    /// and by the lazy FUSE reader.
    /// </summary>
    public static class Epk2SegmentWalker
    {
        private static Pak2DetectionData ReadPakHeader<THeader>(
            ReadOnlySpan64<byte> fileData,
            EpkContext<THeader> ctx,
            int pakLoc
        ) where THeader : struct, IEpkV2Header
        {
            var pak2 = fileData.Slice(pakLoc, THeader.PakStructureSize);
            var pakHeaderBytes = THeader.GetPakHeaderBytes(pak2);
            var handler = new Pak2Handler<THeader>(ctx);

            var pakResult = handler.Detect(pakHeaderBytes);
            if (!pakResult.Succeded())
            {
                throw new InvalidDataException("Invalid PAK2 header, or decryption failed");
            }
            return (Pak2DetectionData)pakResult.Context!;
        }

        public static List<Epk2PakImage> BuildPakImages<THeader>(
            ReadOnlySpan64<byte> fileData,
            EpkContext<THeader> ctx,
            THeader hdr
        ) where THeader : struct, IEpkV2Header
        {
            var images = new List<Epk2PakImage>();
            int signatureSize = THeader.SignatureSize;
            int pakStructureSize = THeader.PakStructureSize;

            int numSignatures = 1; // header signature
            for (int curPak = 0; curPak < hdr.FileNum; curPak++)
            {
                int pakLoc = (int)hdr.GetImageOffset(curPak) + (numSignatures * signatureSize);

                var segments = new List<PakSegment>();
                PAK_V2_HEADER firstHeader = default;
                int segmentCount = 0;

                while (true)
                {
                    var pak2 = ReadPakHeader<THeader>(fileData, ctx, pakLoc);
                    var pakHdr = pak2.Header;

                    if (segments.Count == 0)
                    {
                        firstHeader = pakHdr;
                        segmentCount = (int)pakHdr.segmentCount;
                    }

                    long dataStart = pakLoc + pakStructureSize;
                    long segSize = pakHdr.segmentSize;

                    segments.Add(new PakSegment
                    {
                        PhysicalDataOffset = dataStart,
                        EncryptedSize = segSize,
                        ContentOffset = 0,
                        // Full stored size: AES covers whole 16-byte blocks only;
                        // a trailing (size % 16) remainder is stored raw and
                        // passed through at read time (same split as PakStream).
                        LogicalSize = segSize,
                        WasDecrypted = pak2.WasDecrypted,
                        Decryptor = pak2.WasDecrypted
                            ? ctx.GetDecryptor(PAK_V2_HEADER.PAK_MAGIC)
                            : null
                    });

                    if (pakHdr.segmentIndex + 1 == pakHdr.segmentCount) break;
                    pakLoc += pakStructureSize + (int)segSize;
                }

                images.Add(new Epk2PakImage
                {
                    Header = firstHeader,
                    SegmentCount = segmentCount,
                    Image = new PakImage
                    {
                        Name = $"{firstHeader.ImageType}.pak",
                        Segments = segments
                    }
                });

                numSignatures += segmentCount;
            }

            return images;
        }

        /// <summary>
        /// Reads the logical content of a segment back from the source.
        /// Used by the standalone extractor to materialize output.
        /// AES covers whole 16-byte blocks only; a trailing remainder is
        /// stored raw and appended verbatim (mirrors PakStream.ReadSegment).
        /// </summary>
        public static byte[] ReadSegmentContent(PakSegment seg, ReadOnlySpan64<byte> fileData)
        {
            if (seg.WasDecrypted && seg.Decryptor != null)
            {
                long segTotal = seg.ContentOffset + seg.LogicalSize;
                long encLen = segTotal & ~0x0FL;
                var encrypted = fileData.Slice(seg.PhysicalDataOffset, seg.EncryptedSize);
                if (encLen > seg.ContentOffset)
                {
                    var cipherPrefix = encrypted.Slice(0, encLen);
                    var decrypted = seg.Decryptor.Decrypt(cipherPrefix);
                    var head = decrypted.Slice(seg.ContentOffset, encLen - seg.ContentOffset).ToArray();
                    if (segTotal == encLen) return head;
                    var tail = encrypted.Slice(encLen, segTotal - encLen).ToArray();
                    var full = new byte[head.Length + tail.Length];
                    Array.Copy(head, full, head.Length);
                    Array.Copy(tail, 0, full, head.Length, tail.Length);
                    return full;
                }
                return encrypted.Slice(seg.ContentOffset, seg.LogicalSize).ToArray();
            }
            return fileData.Slice(seg.PhysicalDataOffset + seg.ContentOffset, seg.LogicalSize).ToArray();
        }
    }
}
