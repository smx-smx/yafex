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

using Yafex.FileFormats.Uefi;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for UEFI firmware images: flash-descriptor regions (when
/// present) split as slices, firmware volumes / FFS files / sections as
/// nested lazy directories (zero-copy slices), compressed sections
/// decompressed on open, nested volumes recursed with a depth cap.
/// </summary>
public sealed class UefiExpander : IContentExpander
{
    public string Name => "uefi";

    private const int MaxDepth = 4;

    public bool Matches(IVirtualFile file)
    {
        if (file.Length < 64)
        {
            return false;
        }
        if (ExpanderSniff.Head(file, 32, Uefi.HasFlashDescriptorPrefix))
        {
            return true;
        }
        return ExpanderSniff.At(file, 0, (int)Math.Min(file.Length, 1 << 26), HasVolume);
    }

    private static bool HasVolume(byte[] window)
    {
        for (int off = 0; off + 56 <= window.Length; off++)
        {
            if (window[off + 40] == '_' && window[off + 41] == 'F'
                && window[off + 42] == 'V' && window[off + 43] == 'H'
                && Uefi.TryGetVolumeAt(window, off, out _))
            {
                return true;
            }
        }
        return false;
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            var image = ReadAll(file);
            var nodes = new List<IVfsNode>();
            var regions = Uefi.ReadRegions(image);
            if (regions.Count == 0)
            {
                AddVolumesDir(nodes, file, image, "bios", keepAlive, depth: 0);
            }
            else
            {
                foreach (var r in regions)
                {
                    var slice = new SliceVirtualFile($"region-{r.Name}.bin", file, r.Offset, r.Length);
                    keepAlive.Add(slice);
                    nodes.Add(new PakVfsFile(slice, Helpers.OctalLiteral(0444)));
                    AddVolumesDir(nodes, slice, Slice(image, r.Offset, r.Length),
                        r.Name, keepAlive, depth: 0);
                }
            }
            return nodes;
        });
    }

    // A `<name>.d` directory holding the firmware volumes of one region
    // image. `backing` covers exactly `window` (zero-copy slices below).
    private static void AddVolumesDir(List<IVfsNode> nodes, IVirtualFile backing,
        byte[] window, string name, ICollection<object> keepAlive, int depth)
    {
        var volumes = Uefi.FindVolumes(window);
        if (volumes.Count == 0)
        {
            return;
        }
        nodes.Add(new LazyVfsDirectory(name + ".d", Helpers.OctalLiteral(0755), () =>
            volumes.Select((v, vi) => FvDir(backing, window, v, vi, keepAlive, depth)).ToList()));
    }

    private static IVfsNode FvDir(IVirtualFile backing, byte[] window,
        Uefi.FirmwareVolume vol, int index, ICollection<object> keepAlive, int depth)
    {
        return new LazyVfsDirectory($"fv-{index:D2}@{vol.Offset:X}", Helpers.OctalLiteral(0755), () =>
        {
            var nodes = new List<IVfsNode>();
            var files = Uefi.ReadFiles(window, vol);
            for (int fi = 0; fi < files.Count; fi++)
            {
                nodes.Add(FileDir(backing, window, files[fi], fi, keepAlive, depth));
            }
            return nodes;
        });
    }

    private static IVfsNode FileDir(IVirtualFile backing, byte[] window,
        Uefi.FfsFile file, int seq, ICollection<object> keepAlive, int depth)
    {
        string? ui = null;
        foreach (var s in Uefi.ReadSections(window, file, cap: 8))
        {
            if (s.Type == 0x15 && Uefi.ReadUiName(window, s) is string name)
            {
                ui = UefiStructures.SanitizeUi(name);
                break;
            }
        }
        string dirName = $"{seq:D4}-{UefiStructures.ShortGuid(file.Name)}"
            + $"-{UefiStructures.FileTypeName(file.Type)}" + (ui != null ? $"-{ui}" : "");
        return new LazyVfsDirectory(dirName, Helpers.OctalLiteral(0755), () =>
        {
            var nodes = new List<IVfsNode>();
            foreach (var s in Uefi.ReadSections(window, file))
            {
                string secName = UefiStructures.SectionTypeName(s.Type);
                if (s.Type == 0x17 && depth < MaxDepth)
                {
                    // nested firmware volume image: recurse via the engine shape
                    var raw = Uefi.SectionBytes(window, s);
                    var mem = new MemoryVirtualFile($"{secName}.fv", raw);
                    keepAlive.Add(mem);
                    nodes.Add(new PakVfsFile(mem, Helpers.OctalLiteral(0444)));
                    var sub = new List<IVfsNode>();
                    AddVolumesDir(sub, mem, raw, secName, keepAlive, depth + 1);
                    foreach (var n in sub)
                    {
                        nodes.Add(n);
                    }
                }
                else if (s.Type == 0x01 && depth < MaxDepth
                    && Uefi.TryReadCompress(window, s, out var info))
                {
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
                        AddSlice(nodes, backing, s, secName + ".cmp", keepAlive);
                    }
                    else if (Uefi.InnerFirmwareVolumes(inner).Count > 0)
                    {
                        var mem = new MemoryVirtualFile($"{secName}.fv", inner);
                        keepAlive.Add(mem);
                        nodes.Add(new PakVfsFile(mem, Helpers.OctalLiteral(0444)));
                        var sub = new List<IVfsNode>();
                        AddVolumesDir(sub, mem, inner, secName, keepAlive, depth + 1);
                        foreach (var n in sub)
                        {
                            nodes.Add(n);
                        }
                    }
                    else
                    {
                        var pseudo = new Uefi.FfsFile(0, Guid.Empty, 0, 0, inner.Length, 0, 0, 0);
                        foreach (var innerSec in Uefi.ReadSections(inner, pseudo))
                        {
                            var mem = new MemoryVirtualFile(
                                $"{UefiStructures.SectionTypeName(innerSec.Type)}.bin",
                                Uefi.SectionBytes(inner, innerSec));
                            keepAlive.Add(mem);
                            nodes.Add(new PakVfsFile(mem, Helpers.OctalLiteral(0444)));
                        }
                    }
                }
                else
                {
                    AddSlice(nodes, backing, s, secName + ".bin", keepAlive);
                }
            }
            return nodes;
        });
    }

    private static void AddSlice(List<IVfsNode> nodes, IVirtualFile backing,
        Uefi.Section s, string name, ICollection<object> keepAlive)
    {
        var slice = new SliceVirtualFile(name, backing, s.Offset, s.Size);
        keepAlive.Add(slice);
        nodes.Add(new PakVfsFile(slice, Helpers.OctalLiteral(0444)));
    }

    private static byte[] Slice(byte[] image, long offset, long length)
    {
        var out_ = new byte[length];
        Buffer.BlockCopy(image, (int)offset, out_, 0, (int)length);
        return out_;
    }

    private static byte[] ReadAll(IVirtualFile file)
    {
        if (file.Length > 1 << 28)
        {
            throw new InvalidDataException("UEFI image too large");
        }
        return file.ReadAllBytes();
    }
}
