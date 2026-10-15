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
using log4net;

using Smx.SharpIO;
using Smx.SharpIO.Extensions;
using Smx.SharpIO.Memory.Buffers;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Yafex.Support;

namespace Yafex.FileFormats.LxBoot
{
    public struct SpbcDescriptor
    {
        public UInt32 unk0 { get; set; } // 0x4
        public UInt32 LoadAddress { get; set; } // 0xC10806
        public UInt32 unk1 { get; set; } // 0 or 0xA (CPU revision #1)?
        public UInt32 unk2 { get; set; } // 0 or 0xB (CPU revision #2)?

    }

    public enum SpbcVariant
    {
        Unknown = 0,
        Old = 1,
        New = 2,
        // Bare second-stage image (branch + LL/B_LL header at offset 0,
        // no SPBC wrapper): e.g. webos lxboot.pak.
        Bare = 3,
        // LL image contained at a nonzero offset (found by scan, e.g.
        // lxboot.bin with an AArch64 LL header at 0x22000): carved out
        // like Bare instead of processed as SPBC.
        Embedded = 4
    }

    internal class LxSecureBootDetector : IFormatDetector
    {
        private readonly ILog logger = LogManager.GetLogger(typeof(LxSecureBootDetector));

        // Compat with Smx.SharpIO 2.0-beta.3 (no TryRead* on SpanStream):
        // check Remaining first, then Read* inside try/catch.
        private static bool TryReadUInt32(SpanStream reader, out uint value)
        {
            value = 0;
            try
            {
                if (reader.Remaining < sizeof(uint))
                {
                    return false;
                }
                value = reader.ReadUInt32();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadSpbcDescriptor(SpanStream reader, out SpbcDescriptor value)
        {
            value = default;
            if (!TryReadUInt32(reader, out var unk0)) return false;
            if (!TryReadUInt32(reader, out var loadAddress)) return false;
            if (!TryReadUInt32(reader, out var unk1)) return false;
            if (!TryReadUInt32(reader, out var unk2)) return false;
            value = new SpbcDescriptor
            {
                unk0 = unk0,
                LoadAddress = loadAddress,
                unk1 = unk1,
                unk2 = unk2,
            };
            return true;
        }

        private bool IsKnownMagic(string magic)
        {
            switch (magic)
            {
                // chip revision (NOTE: "\x13A0" would parse as U+13A0;
                // split the escape so the labels stay 4 chars)
                case "H\x13" + "A0":
                case "M\x14" + "B0":
                // platform
                case "M\x16PR":
                case "O\x18PR":
                case "O\x20PR":
                    return true;
            }
            return false;
        }

        private const string SIC_MAGIC = "SIC!";

        private const uint LLB_MAGIC = 0x4C4C5F42;
        private const uint BLL_MAGIC = 0x425F4C4C;

        /// <summary>
        /// Branch-opcode check for LL headers (smx-smx/sb_verify LX
        /// LLHeader: {branch, magic "LL_B", load, size}). ARM32 B (0xEA)
        /// plus AArch64 B/BL (0x14/0x94): real images are AArch64 (e.g.
        /// lxboot.bin header 0x14000300 = B #0xC00), ARM32 BL unobserved.
        /// </summary>
        internal static bool IsBranchInstruction(uint branch)
        {
            byte top = (byte)(branch >> 24);
            return top == 0xEA || top == 0x14 || top == 0x94;
        }

        /// <summary>
        /// Bare LL second stage at offset 0: ARM branch, LL/B_LL magic,
        /// fitting size. Header layout ({branch, magic, load, size}) matches
        /// smx-smx/sb_verify's LLHeader documentation.
        /// </summary>
        internal static bool TryReadBareSecondStage(Memory64<byte> data, out uint size)
        {
            size = 0;
            try
            {
                if (data.Length < 16)
                {
                    return false;
                }
                var head = data.Slice(0, 16).ToArray();
                uint branch = BitConverter.ToUInt32(head, 0);
                uint magic = BitConverter.ToUInt32(head, 4);
                size = BitConverter.ToUInt32(head, 12);
                if (!IsBranchInstruction(branch))
                {
                    return false;
                }
                if (magic != LLB_MAGIC && magic != BLL_MAGIC)
                {
                    return false;
                }
                if (size < 16 || (long)size > data.Length)
                {
                    return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// LL images contained at nonzero offsets (4-aligned scan): same
        /// header acceptance as bare (branch shape, either magic, fitting
        /// size). Offset 0 is excluded (handled by the Bare path).
        /// </summary>
        internal static List<(uint Offset, uint Size)> FindEmbeddedImages(Memory64<byte> data)
        {
            // magic-first sliced scan: header windows are copied per hit only
            var hits = new List<(uint Offset, uint Size)>();
            try
            {
                foreach (uint magic in new[] { LLB_MAGIC, BLL_MAGIC })
                {
                    int found = 0;
                    foreach (long at in DataScan.FindAll(data, BitConverter.GetBytes(magic), 8))
                    {
                        long off = at - 4;
                        if (off % 4 != 0) continue;
                        var head = DataScan.Window(data, off, 16);
                        if (head.Length < 16 || !IsBranchInstruction(BitConverter.ToUInt32(head, 0))) continue;
                        uint size = BitConverter.ToUInt32(head, 12);
                        if (size < 16 || off + size > data.Length || off > uint.MaxValue) continue;
                        hits.Add(((uint)off, size));
                        if (++found >= 16) break;
                    }
                }
                hits.Sort((a, b) => a.Offset.CompareTo(b.Offset));
                if (hits.Count > 16) hits.RemoveRange(16, hits.Count - 16);
            }
            catch
            {
            }
            return hits;
        }

        private long FindSecondStage(Memory64<byte> data, uint startOffset)
        {
            var mask = 15;
            startOffset = (uint)((startOffset + mask) & ~mask);

            var words = data.Slice(startOffset).Cast<uint>();
            var wordIndex = words.IndexOfAny(BLL_MAGIC, LLB_MAGIC);
            if (wordIndex > -1)
            {
                return (wordIndex * sizeof(uint)) + startOffset - 4;
            }
            return wordIndex;

        }

        public DetectionResult Detect(IDataSource source)
        {
            var data = source.Data;

            using var reader = new SpanStream(data, Endianness.LittleEndian);

            var u4String = (uint v) =>
            {
                return v
                    .Let(v =>
                    {
                        var buf = new byte[4];
                        BinaryPrimitives.WriteUInt32LittleEndian(buf, v);
                        return buf;
                    })
                    .Let(b => Encoding.ASCII.GetString(b));
            };

            var readU4string = (uint offset, out string str) =>
            {
                var res = reader.PerformAt(offset, () =>
                {
                    if(!TryReadUInt32(reader, out var dw))
                    {
                        return null;
                    }
                    return u4String(dw);
                });
                str = res == null
                    ? string.Empty
                    : res;

                return res != null;
            };

            if(!TryReadUInt32(reader, out var dw1)
            || !TryReadUInt32(reader, out var dw2))
            {
                return DetectionResult.Fail;
            }
            var magic1 = u4String(dw1);
            var magic2 = u4String(dw2);

            var confidence = 0;

            SpbcVariant variant = SpbcVariant.Unknown;

            string sicMagic = string.Empty;
            if(readU4string(0xD0, out sicMagic) && sicMagic == SIC_MAGIC)
            {
                variant = SpbcVariant.Old;
            }
            else if (readU4string(0x240, out sicMagic) && sicMagic == SIC_MAGIC)
            {
                variant = SpbcVariant.New;
            }
            else if (TryReadBareSecondStage(data, out uint bareSize))
            {
                var bareCtx = new LxSecureBootContext
                {
                    SpbcVariant = SpbcVariant.Bare,
                    SpbcDescriptors = new System.Collections.Generic.List<SpbcDescriptor>(),
                    SpbcSecondStageOffset = 0,
                };
                logger.InfoFormat("bare LL second stage, size 0x{0:X}", bareSize);
                return new LxSecureBootDetectionResult(60, bareCtx);
            }
            else if (FindEmbeddedImages(data) is var embedded && embedded.Count > 0)
            {
                var embeddedCtx = new LxSecureBootContext
                {
                    SpbcVariant = SpbcVariant.Embedded,
                    SpbcDescriptors = new List<SpbcDescriptor>(),
                    SpbcSecondStageOffset = null,
                    EmbeddedImages = embedded,
                };
                logger.InfoFormat("embedded LL image(s): {0}", embedded.Count);
                return new LxSecureBootDetectionResult(60, embeddedCtx);
            }
            else
            {
                return DetectionResult.Fail;
            }

            var descrsOffset = variant switch
            {
                SpbcVariant.Old => 0x400,
                SpbcVariant.New => 0x500,
                _ => throw new NotSupportedException(),
            };

            confidence += 20;
            confidence += IsKnownMagic(magic1) ? 20 : 0;
            confidence += IsKnownMagic(magic2) ? 20 : 0;

            var descrsPairs = reader.PerformAt(descrsOffset, () =>
            {
                return Enumerable.Range(0, 2)
                    .Select(_ => {
                        var res = TryReadSpbcDescriptor(reader, out SpbcDescriptor descr);
                        return (res, descr);
                    })
                    .ToList();
            });

            if (descrsPairs.Any(x => !x.res))
            {
                return DetectionResult.Fail;
            }

            var descrs = descrsPairs.Select(x => x.descr).ToList();

            var spbcLoadAddress = descrs.First().LoadAddress;
            logger.InfoFormat("SPBC Load Address: 0x{0:X}", spbcLoadAddress);

            var spbcOffset = spbcLoadAddress & 0xFFFF;
            var secondStageOffset = FindSecondStage(data, spbcOffset);

            var ctx = new LxSecureBootContext
            {
                SpbcVariant = variant,
                SpbcDescriptors = descrs,
                SpbcSecondStageOffset = (secondStageOffset < 0) ? null : (uint)secondStageOffset,
            };

            return new LxSecureBootDetectionResult(confidence, ctx);
        }
    }
}
