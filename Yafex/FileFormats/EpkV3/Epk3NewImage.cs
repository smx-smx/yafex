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
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using Smx.SharpIO.Extensions;
using Smx.SharpIO.Memory.Buffers;

using Yafex.Support;

namespace Yafex.FileFormats.EpkV3
{
    /// <summary>
    /// A package inside an EPK3 ("new" variant) image, described lazily through its segment map.
    /// </summary>
    public class Epk3NewPak
    {
        public string Name = "";
        public PakImage Image = null!;
        public PAK_V3_HEADER Header;
    }

    /// <summary>
    /// Parses the EPK3 package info and walks the flat encrypted chunk stream, building
    /// <see cref="PakImage"/> descriptors without materializing any package payload.
    /// The physical chunk order reproduces the standalone extractor's iteration order
    /// exactly, so lazy reads and full extraction produce identical bytes.
    /// </summary>
    public class Epk3NewImage
    {
        public readonly List<Epk3NewPak> Paks = new();

        public uint PakInfoMagic { get; }

        public Epk3NewImage(Epk3NewContext ctx, ReadOnlySpan64<byte> data, EPK_V3_NEW_STRUCTURE epk)
        {
            var hdr = ctx.Header;
            var decryptor = ctx.GetDecryptor(EPK_V3_NEW_HEADER.EPK3_MAGIC);
            bool isOld = ctx.Variant == Epk3Variant.OLD;

            // Old: head | 128B sig | list(size) | chunks
            // New: head | 256B sig+list(size incl sig?) | chunks (existing behavior, verified)
            int packageInfoOffset;
            int signatureSize;
            // encrypted region holding the package list
            long packageInfoLength;
            if (isOld)
            {
                packageInfoOffset = Marshal.SizeOf<EPK_V3_HEAD_STRUCTURE>()
                    + EPK_V3_HEAD_STRUCTURE.SIGNATURE_SIZE;
                signatureSize = EPK_V3_HEAD_STRUCTURE.SIGNATURE_SIZE;
                packageInfoLength = hdr.packageInfoSize;
            }
            else
            {
                // New: the 256-byte list signature precedes the list
                // (PAK_V3_NEW_LISTHEADER starts with it) and is not counted
                // in packageInfoSize, which covers header + entries only
                packageInfoOffset = Marshal.OffsetOf<EPK_V3_NEW_STRUCTURE>(nameof(epk.packageInfo)).ToInt32();
                signatureSize = EPK_V3_NEW_STRUCTURE.SIGNATURE_SIZE;
                packageInfoLength = (long)signatureSize + hdr.packageInfoSize;
            }
            long chunksStart = packageInfoOffset + packageInfoLength;
            var packageInfoBytes = data.Slice(packageInfoOffset, packageInfoLength);
            var decrypted = decryptor.Decrypt(packageInfoBytes);

            uint packageInfoCount;
            int listHeaderSize;
            if (isOld)
            {
                var oldList = decrypted.ReadStruct<PAK_V3_LISTHEADER>();
                packageInfoCount = oldList.packageInfoCount;
                listHeaderSize = Marshal.SizeOf<PAK_V3_LISTHEADER>();
                PakInfoMagic = 0;
            }
            else
            {
                var listHeader = decrypted.ReadStruct<PAK_V3_NEW_LISTHEADER>();
                PakInfoMagic = listHeader.pakInfoMagic;
                packageInfoCount = listHeader.packageInfoCount;
                listHeaderSize = Marshal.SizeOf<PAK_V3_NEW_LISTHEADER>();
            }

            // sanity: corrupt decrypts yield huge counts -> fail fast, no OOM
            if (packageInfoCount > 10000
                || (long)listHeaderSize + (long)packageInfoCount * Marshal.SizeOf<PAK_V3_HEADER>() > decrypted.Length)
            {
                throw new InvalidDataException(
                    $"EPK3 packageInfoCount {packageInfoCount} out of range (variant {(isOld ? "old" : "new")})");
            }

            var packages = GetPackageInfoPackages(decrypted, (int)packageInfoCount, listHeaderSize)
                .GroupBy(p => p.packageName.AsString(Encoding.ASCII))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(p => p).ToList()
                );

            long cur = chunksStart;

            foreach (var pak in packages)
            {
                var name = pak.Key;
                var firstIndex = (int)pak.Value.First().segmentInfo.segmentIndex;
                var segmentCount = (int)pak.Value.First().segmentInfo.segmentCount;

                var image = new PakImage { Name = $"{name}.pak" };

                var i = 0;
                foreach (var chunk in pak.Value)
                {
                    if (firstIndex + i >= segmentCount) break;

                    // New chunks: sig + u32 index + payload (index verified on extract).
                    // Old chunks: sig + payload, no index prefix (see epk2extract).
                    long dataSize = isOld
                        ? chunk.segmentInfo.segmentSize
                        : sizeof(uint) + chunk.segmentInfo.segmentSize;
                    long chunkDataStart = cur + signatureSize;
                    int contentOffset = isOld ? 0 : sizeof(uint);

                    image.Segments.Add(new PakSegment
                    {
                        PhysicalDataOffset = chunkDataStart,
                        EncryptedSize = dataSize,
                        ContentOffset = contentOffset,
                        // Full payload: AES covers whole blocks, trailing
                        // (dataSize % 16) bytes are stored raw (see ReadSegment).
                        LogicalSize = dataSize - contentOffset,
                        WasDecrypted = true,
                        Decryptor = decryptor
                    });

                    cur += signatureSize + dataSize;
                    ++i;
                }

                if (image.Segments.Count > 0)
                {
                    Paks.Add(new Epk3NewPak
                    {
                        Name = name,
                        Image = image,
                        Header = pak.Value.First()
                    });
                }
            }
        }

        private IEnumerable<PAK_V3_HEADER> GetPackageInfoPackages(Memory64<byte> packageInfo, int packageInfoCount, int listHeaderSize)
        {
            var listOffset = listHeaderSize;
            var sizeOfPak = Marshal.SizeOf<PAK_V3_HEADER>();
            for (int i = 0; i < packageInfoCount; i++)
            {
                var pak = packageInfo.ReadStruct<PAK_V3_HEADER>(listOffset);
                yield return pak;
                listOffset += sizeOfPak;
            }
        }
    }
}
