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
using System.Text;

namespace Yafex.FileFormats.Uefi;

public record UefiDetectionResult(int Confidence, bool HasFlashDescriptor, int Volumes, int Files)
    : DetectionResult(Confidence);

public class UefiDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 64 || !Uefi.MayContainFirmware(data))
            {
                return DetectionResult.Fail;
            }
            var bytes = data.ToArray();
            if (Uefi.HasFlashDescriptor(bytes))
            {
                var regions = Uefi.ReadRegions(bytes);
                if (regions.Count > 0)
                {
                    return new UefiDetectionResult(90, true, 0, 0);
                }
            }
            var volumes = Uefi.FindVolumes(bytes);
            if (volumes.Count == 0)
            {
                // FIT with magic-verified microcode is a weaker signal.
                int ucodes = Uefi.ReadFit(bytes)
                    .Count(e => e.Type == 0x01 && Uefi.TryReadMicrocode(bytes, e, out _));
                if (ucodes == 0)
                {
                    return DetectionResult.Fail;
                }
                return new UefiDetectionResult(70, false, 0, 0);
            }
            int files = 0;
            var first = volumes[0];
            if (UefiStructures.IsFfsFileSystem(first.FsGuid))
            {
                files = Uefi.ReadFiles(bytes, first, cap: 32).Count;
            }
            int confidence = Math.Min(90, 70 + Math.Min(10, 5 * volumes.Count) + (files > 0 ? 10 : 0));
            return new UefiDetectionResult(confidence, false, volumes.Count, files);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class UefiExtractor : IFormatExtractor
{
    private readonly UefiDetectionResult _result;

    public UefiExtractor(UefiDetectionResult result)
    {
        _result = result;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var bytes = source.Data.ToArray();
        string baseDir = ArtifactBuilder.VersionedDir(source, "uefi");

        // Intel flash descriptor: split the SPI partitions first (the BIOS
        // region re-enters this format via ProcessFurther).
        var regions = Uefi.ReadRegions(bytes);
        byte[] bios = bytes;
        if (regions.Count > 0)
        {
            foreach (var r in regions)
            {
                var blob = new byte[r.Length];
                Buffer.BlockCopy(bytes, (int)r.Offset, blob, 0, blob.Length);
                var part = ArtifactBuilder.FlatIn(source, baseDir, $"region-{r.Name}.bin", blob);
                if (r.Name == "bios")
                {
                    part.Flags |= DataSourceFlags.ProcessFurther;
                }
                yield return part;
            }
            var biosRegion = regions.FirstOrDefault(r => r.Name == "bios");
            if (biosRegion != null)
            {
                bios = new byte[biosRegion.Length];
                Buffer.BlockCopy(bytes, (int)biosRegion.Offset, bios, 0, bios.Length);
            }
        }

        var volumes = Uefi.FindVolumes(bios);
        for (int vi = 0; vi < volumes.Count; vi++)
        {
            var vol = volumes[vi];
            var files = Uefi.ReadFiles(bios, vol);
            for (int fi = 0; fi < files.Count; fi++)
            {
                var file = files[fi];
                string? ui = UiOf(bios, file);
                // volume geometry in the stem: the same (vi, seq) recurs at
                // every nesting level, geometry keeps them distinct
                string stem = $"fv{vi:D2}@{vol.Offset:X}-{vol.Length:X}-{fi:D4}"
                    + $"-{UefiStructures.ShortGuid(file.Name)}"
                    + $"-{UefiStructures.FileTypeName(file.Type)}"
                    + (ui != null ? $"-{ui}" : "");
                foreach (var artifact in EmitFile(source, baseDir, bios, file, stem, depth: 0))
                {
                    yield return artifact;
                }
            }
        }

        // FIT microcode updates (leaf blobs, magic-verified).
        int mi = 0;
        foreach (var e in Uefi.ReadFit(bytes).Where(e => e.Type == 0x01))
        {
            if (Uefi.TryReadMicrocode(bytes, e, out var ucode))
            {
                yield return ArtifactBuilder.FlatIn(
                    source, baseDir, $"fit-{mi:D2}-microcode.bin", ucode);
                mi++;
            }
        }
    }

    private IEnumerable<IDataSource> EmitFile(IDataSource source, string baseDir,
        byte[] img, Uefi.FfsFile file, string stem, int depth)
    {
        foreach (var s in Uefi.ReadSections(img, file))
        {
            foreach (var artifact in EmitSection(source, baseDir, img, s, stem, depth))
            {
                yield return artifact;
            }
        }
    }

    private IEnumerable<IDataSource> EmitSection(IDataSource source, string baseDir,
        byte[] img, Uefi.Section s, string stem, int depth)
    {
        string typeName = UefiStructures.SectionTypeName(s.Type);
        switch (s.Type)
        {
            case 0x15: // UI: text sidecar
            {
                var ui = Uefi.ReadUiName(img, s) ?? "";
                yield return ArtifactBuilder.FlatIn(source, baseDir, $"{stem}-{typeName}.txt",
                    Encoding.UTF8.GetBytes(ui));
                break;
            }
            case 0x14: // VERSION: build number + text
            {
                string text = "";
                if (s.PayloadSize >= 2)
                {
                    int build = img[s.PayloadOffset] | (img[s.PayloadOffset + 1] << 8);
                    string name = "";
                    try
                    {
                        name = Encoding.Unicode.GetString(img, (int)s.PayloadOffset + 2,
                            s.PayloadSize - 2).TrimEnd('\0');
                    }
                    catch (ArgumentException)
                    {
                    }
                    text = $"build={build} name={name}";
                }
                yield return ArtifactBuilder.FlatIn(source, baseDir, $"{stem}-{typeName}.txt",
                    Encoding.UTF8.GetBytes(text));
                break;
            }
            case 0x17: // nested firmware volume: recurse via the engine
            {
                var fv = Uefi.SectionBytes(img, s);
                var artifact = ArtifactBuilder.FlatIn(source, baseDir, $"{stem}-{typeName}.fv", fv);
                artifact.Flags |= DataSourceFlags.ProcessFurther;
                yield return artifact;
                break;
            }
            case 0x01: // compressed: unpack and walk the inner sections
            {
                if (!Uefi.TryReadCompress(img, s, out var info))
                {
                    break;
                }
                byte[]? inner = null;
                try
                {
                    inner = Uefi.DecompressSection(info);
                }
                catch (InvalidDataException)
                {
                }
                if (inner == null)
                {
                    yield return ArtifactBuilder.FlatIn(source, baseDir, $"{stem}-{typeName}.cmp",
                        Uefi.SectionBytes(img, s));
                    break;
                }
                if (Uefi.TryGetVolumeAt(inner, 0, out _)
                    || Uefi.InnerFirmwareVolumes(inner).Count > 0)
                {
                    var artifact = ArtifactBuilder.FlatIn(source, baseDir, $"{stem}-{typeName}.fv", inner);
                    artifact.Flags |= DataSourceFlags.ProcessFurther;
                    yield return artifact;
                    break;
                }
                if (depth < 3)
                {
                    var pseudo = new Uefi.FfsFile(0, Guid.Empty, 0, 0, inner.Length, 0, 0, 0);
                    foreach (var innerSec in Uefi.ReadSections(inner, pseudo))
                    {
                        foreach (var artifact in EmitSection(source, baseDir, inner,
                            innerSec, $"{stem}-d{depth}", depth + 1))
                        {
                            yield return artifact;
                        }
                    }
                }
                break;
            }
            case 0x02: // GUID-defined: dispatch by definition GUID
            {
                if (!Uefi.TryReadGuided(img, s, out var guided))
                {
                    break;
                }
                byte[]? inner = null;
                try
                {
                    inner = Uefi.DecompressGuided(img, s, guided);
                }
                catch (InvalidDataException)
                {
                }
                if (inner == null)
                {
                    yield return ArtifactBuilder.FlatIn(source, baseDir,
                        $"{stem}-{typeName}-{UefiStructures.ShortGuid(guided.DefinitionGuid)}.guided",
                        Uefi.SectionBytes(img, s));
                    break;
                }
                if (guided.DefinitionGuid == UefiStructures.GuidedFvImage
                    || Uefi.TryGetVolumeAt(inner, 0, out _)
                    || Uefi.InnerFirmwareVolumes(inner).Count > 0)
                {
                    var artifact = ArtifactBuilder.FlatIn(source, baseDir,
                        $"{stem}-{typeName}.fv", inner);
                    artifact.Flags |= DataSourceFlags.ProcessFurther;
                    yield return artifact;
                }
                else if (depth < 3)
                {
                    var pseudo = new Uefi.FfsFile(0, Guid.Empty, 0, 0, inner.Length, 0, 0, 0);
                    foreach (var innerSec in Uefi.ReadSections(inner, pseudo))
                    {
                        foreach (var artifact in EmitSection(source, baseDir, inner,
                            innerSec, $"{stem}-d{depth}", depth + 1))
                        {
                            yield return artifact;
                        }
                    }
                }
                break;
            }
            default: // PE32/TE/RAW/DEPEX/...: leaf payloads
            {
                string ext = s.Type switch
                {
                    0x10 => ".pe32",
                    0x11 => ".pic",
                    0x12 => ".te",
                    _ => ".bin",
                };
                yield return ArtifactBuilder.FlatIn(source, baseDir, $"{stem}-{typeName}{ext}",
                    Uefi.SectionBytes(img, s));
                break;
            }
        }
    }

    private static string? UiOf(byte[] img, Uefi.FfsFile file)
    {
        foreach (var s in Uefi.ReadSections(img, file, cap: 8))
        {
            if (s.Type == 0x15)
            {
                var ui = Uefi.ReadUiName(img, s);
                if (ui != null)
                {
                    return UefiStructures.SanitizeUi(ui);
                }
            }
        }
        return null;
    }
}
