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

using System;
using System.Buffers.Binary;
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
        New = 2
    }

    internal class LxSecureBootDetector : IFormatDetector
    {
        private readonly ILog logger = LogManager.GetLogger(typeof(LxSecureBootDetector));

        private bool IsKnownMagic(string magic)
        {
            switch (magic)
            {
                // chip revision
                case "H\x13A0":
                case "M\x14B0":
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

        private int FindSecondStage(Memory<byte> data, uint startOffset)
        {
            var mask = 15;
            startOffset = (uint)((startOffset + mask) & ~mask);

            var words = data.Slice((int)startOffset).Cast<uint>();
            var wordIndex = words.IndexOfAny(BLL_MAGIC, LLB_MAGIC);
            if (wordIndex > -1)
            {
                return (int)((wordIndex * sizeof(uint)) + startOffset - 4);
            }
            return wordIndex;

        }

        public DetectionResult Detect(IDataSource source)
        {
            var data = source.Data;

            using var reader = new SpanStream(data)
                .Also(it => it.Endianness = Endianness.LittleEndian);

            var u4String = (uint v) =>
            {
                return v
                    .Let(v =>
                    {
                        var buf = new byte[4];
                        BinaryPrimitives.WriteUInt32BigEndian(buf, v);
                        return buf;
                    })
                    .Let(b => Encoding.ASCII.GetString(b));
            };

            var readU4string = (uint offset) =>
            {
                return reader.PerformAt(offset, () =>
                {
                    return reader.ReadUInt32().Let(u4String);
                });
            };

            var magic1 = reader.ReadUInt32().Let(u4String);
            var magic2 = reader.ReadUInt32().Let(u4String);

            var confidence = 0;

            SpbcVariant variant = SpbcVariant.Unknown;
            if (readU4string(0xD0) == SIC_MAGIC)
            {
                variant = SpbcVariant.Old;
            }
            else if (readU4string(0x240) == SIC_MAGIC)
            {
                variant = SpbcVariant.New;
            }
            else
            {
                return new DetectionResult(0, null);
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

            var descrs = reader.PerformAt(descrsOffset, () =>
            {
                return Enumerable.Range(0, 2)
                    .Select(_ => reader.ReadStruct<SpbcDescriptor>())
                    .ToList();
            });

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

            return new DetectionResult(confidence, ctx);
        }
    }
}
