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
using Yafex.FileFormats.Epk;
using Yafex.Support;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Yafex.Metadata;

namespace Yafex.FileFormats.EpkV1
{
    internal class Epk1Extractor : IFormatExtractor
    {
        private DetectionResult result;

        private readonly Epk1Type epkType;

        private static readonly ILog log = LogManager.GetLogger(nameof(Epk1Extractor));

        public Epk1Extractor(DetectionResult result)
        {
            this.epkType = (Epk1Type)result.Context!;
        }

        private IEnumerable<IDataSource> ExtractEpk1Be(IDataSource source)
        {
            var fileData = source.Data;
            var hdr = fileData.ReadStruct<Epk1BeHeader>();

            Func<int, PakRec> GetPakRec = (int i) =>
            {
                var rec = hdr.pakRecs[i];
                return new PakRec()
                {
                    offset = rec.offset.BigEndianToHost(),
                    size = rec.size.BigEndianToHost()
                };
            };

            Func<PakHeader, PakHeader> AdjustPakHeader = (PakHeader hdr) =>
            {
                hdr.imageSize = hdr.imageSize.BigEndianToHost();
                hdr.swDate = hdr.swDate.BigEndianToHost();
                hdr.swVersion = hdr.swVersion.BigEndianToHost();
                hdr.devMode = (PakBuildMode)((uint)hdr.devMode).BigEndianToHost();
                return hdr;
            };

            string fwVerString;
            {
                var firstPak = AdjustPakHeader(
                    fileData.ReadStruct<PakHeader>((int)GetPakRec(0).offset)
                );
                fwVerString = $"{hdr.EpakVersion}-{firstPak.Platform}";
            }

            var basedir = Path.Combine(source.RequireBaseDirectory(), fwVerString);
            source.AddMetadata(new BaseDirectoryPath(basedir));

            for (int i = 0; i < hdr.PakCount; i++)
            {
                var rec = GetPakRec(i);
                if (rec.offset == 0)
                {
                    continue;
                }

                var pakHdr = fileData.ReadStruct<PakHeader>((int)rec.offset);
                pakHdr = AdjustPakHeader(pakHdr);

                var pakData = fileData.Slice((int)rec.offset + Marshal.SizeOf<PakHeader>(), (int)pakHdr.imageSize);

                var fileName = $"{pakHdr.PakName}.pak";
                var filePath = Path.Combine(basedir, fileName);

                log.Info($"#{i + 1}/{hdr.PakCount} saving PAK (name='{pakHdr.PakName}'," +
                    $" platform='{pakHdr.Platform}'," +
                    $" offset=0x{rec.offset:X}," +
                    $" size='{rec.size}') to file {filePath}");

                var artifact = new MemoryDataSource(pakData.ToArray());
                artifact.SetChildOf(source);
                artifact.AddMetadata(new OutputFileName(fileName));
                artifact.AddMetadata(new OutputDirectoryName(basedir));
                artifact.Flags |= DataSourceFlags.ProcessFurther;
                yield return artifact;
            }
        }

        private IEnumerable<IDataSource> ExtractEpk1Old(IDataSource source)
        {
            var fileData = source.Data;
            var hdr = fileData.ReadStruct<Epk1Header>();

            var basedir = Path.Combine(source.RequireBaseDirectory(), $"{hdr.EpakVersion}-{hdr.OtaID}");
            source.AddMetadata(new BaseDirectoryPath(basedir));

            for (int i = 0; i < hdr.pakCount; i++)
            {
                var rec = hdr.pakRecs[i];

                var pakHdr = fileData.ReadStruct<PakHeader>((int)rec.offset);

                var fileName = $"{pakHdr.PakName}.pak";
                var filePath = Path.Combine(basedir, fileName);

                log.Info($"#{i + 1}/{hdr.pakCount} saving PAK (name='{pakHdr.PakName}'," +
                    $" platform='{pakHdr.Platform}'," +
                    $" offset=0x{rec.offset:X}," +
                    $" size='{rec.size}') to file {filePath}");

                var pakData = fileData.Slice(
                    (int)(rec.offset + Marshal.SizeOf<PakHeader>()),
                    (int)(pakHdr.imageSize)
                );

                var artifact = new MemoryDataSource(pakData.ToArray());
                artifact.SetChildOf(source);
                artifact.Flags |= DataSourceFlags.ProcessFurther;
                artifact.AddMetadata(new OutputFileName(fileName));
                artifact.AddMetadata(new OutputDirectoryName(basedir));
                yield return artifact;
            }
        }

        private IEnumerable<IDataSource> ExtractEpk1New(IDataSource source)
        {
            var fileData = source.Data;
            var hdr = fileData.ReadStruct<Epk1HeaderNew>();
            throw new NotImplementedException();
        }

        public IEnumerable<IDataSource> Extract(IDataSource source)
        {
            var artifacts = epkType switch
            {
                Epk1Type.BigEndian => ExtractEpk1Be(source),
                Epk1Type.Old => ExtractEpk1Old(source),
                Epk1Type.New => ExtractEpk1New(source)
            };
            return artifacts;
        }
    }
}
