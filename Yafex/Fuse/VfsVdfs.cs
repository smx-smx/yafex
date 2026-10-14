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

using Yafex.FileSystems.Vdfs;

namespace Yafex.Fuse;

/// <summary>
/// Shared VDFS helpers: probe an <see cref="IVirtualFile"/> for a VDFS
/// volume (volume-begins signature plus superblock magic) and expose its
/// tree as VFS nodes. Reads go through the virtual file, so nested
/// filesystems never require extraction.
/// </summary>
public static class VfsVdfs
{
    public static bool TryOpen(
        IVirtualFile image, ICollection<object> keepAlive, out VdfsFileSystem fs)
    {
        fs = null!;
        try
        {
            using var probe = image.OpenRead();
            if (probe.Length < 4096) return false;
            var head = new byte[4096];
            int off = 0;
            while (off < head.Length)
            {
                int n = probe.Read(head, off, head.Length - off);
                if (n <= 0) return false;
                off += n;
            }
            if (!IsVolume(head)) return false;

            var stream = image.OpenRead();
            var vdfs = new VdfsFileSystem(stream);
            // keep the fs (and its stream) alive for the whole mount session
            keepAlive.Add(vdfs);
            fs = vdfs;
            return true;
        }
        catch (Exception ex)
        {
            log.Debug($"vdfs open of '{image.Name}' failed", ex);
            return false;
        }
    }

    public static bool IsVolume(byte[] head)
    {
        if (head.Length < 4096) return false;
        if (head[0] != 'V' || head[1] != 'D' || head[2] != 'F' || head[3] != 'S') return false;
        string layout = Encoding.ASCII.GetString(head, 4, 4);
        if (layout != "2007" && layout != "2006") return false;
        const int sb = 1024;
        return head[sb] == 'V' && head[sb + 1] == 'D' && head[sb + 2] == 'F' && head[sb + 3] == 'S';
    }

    private static readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof(VfsVdfs));
}

/// <summary>Minimal GPT probe: protective MBR is ignored, the partition
/// array comes from the header at LBA 1. Sector size is fixed at 512.</summary>
public static class VfsGpt
{
    public const int SectorSize = 512;
    public const long HeaderOffset = SectorSize;

    public sealed record GptPartition(string Name, long FirstLba, long LastLba);

    public static bool TryReadPartitions(IVirtualFile image, out List<GptPartition> partitions)
    {
        partitions = new List<GptPartition>();
        try
        {
            using var probe = image.OpenRead();
            if (probe.Length < HeaderOffset + 92) return false;
            var hdr = new byte[92];
            probe.Seek(HeaderOffset, SeekOrigin.Begin);
            int off = 0;
            while (off < hdr.Length)
            {
                int n = probe.Read(hdr, off, hdr.Length - off);
                if (n <= 0) return false;
                off += n;
            }
            if (!IsHeader(hdr, out uint entryCount, out uint entrySize, out long entryLba)) return false;

            long tableBytes = (long)entryCount * entrySize;
            if (tableBytes <= 0 || tableBytes > 1 << 24) return false;
            if (entryLba < 0 || entryLba > (probe.Length >> 9)) return false;
            var table = new byte[tableBytes];
            probe.Seek(entryLba * SectorSize, SeekOrigin.Begin);
            off = 0;
            while (off < table.Length)
            {
                int n = probe.Read(table, off, table.Length - off);
                if (n <= 0) return false;
                off += n;
            }
            for (uint i = 0; i < entryCount; i++)
            {
                int at = (int)(i * entrySize);
                bool empty = true;
                for (int k = 0; k < 16; k++)
                {
                    if (table[at + k] != 0) { empty = false; break; }
                }
                if (empty) continue;
                long first = BitConverter.ToInt64(table, at + 32);
                long last = BitConverter.ToInt64(table, at + 40);
                if (first < 0 || last < first || (last << 9) >= probe.Length) continue;
                string name = DecodeName(table, at + 56, (int)entrySize - 56).TrimEnd('\0', ' ');
                if (string.IsNullOrEmpty(name)) name = $"part{i}";
                partitions.Add(new GptPartition(name, first, last));
            }
            return partitions.Count > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool IsHeader(byte[] hdr, out uint entryCount, out uint entrySize, out long entryLba)
    {
        entryCount = 0;
        entrySize = 0;
        entryLba = 0;
        if (hdr.Length < 92) return false;
        if (hdr[0] != 'E' || hdr[1] != 'F' || hdr[2] != 'I' || hdr[3] != ' '
            || hdr[4] != 'P' || hdr[5] != 'A' || hdr[6] != 'R' || hdr[7] != 'T') return false;
        if (BitConverter.ToUInt32(hdr, 8) != 0x00010000) return false;
        if (BitConverter.ToUInt32(hdr, 12) < 92) return false;
        entryCount = BitConverter.ToUInt32(hdr, 80);
        entrySize = BitConverter.ToUInt32(hdr, 84);
        entryLba = BitConverter.ToInt64(hdr, 72);
        if (entryCount == 0 || entryCount > 1024) return false;
        if (entrySize < 128 || entrySize > 1024) return false;
        if (entryLba < 0) return false;
        return true;
    }

    private static string DecodeName(byte[] table, int at, int maxLen)
    {
        int len = maxLen - (maxLen % 2);
        if (at + len > table.Length) len = table.Length - at;
        try
        {
            return Encoding.Unicode.GetString(table, at, len);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}

public sealed class GptExpander : IContentExpander
{
    public string Name => "gpt";

    public bool Matches(IVirtualFile file)
    {
        return ExpanderSniff.At(file, VfsGpt.HeaderOffset, 92,
            hdr => VfsGpt.IsHeader(hdr, out _, out _, out _));
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            if (!VfsGpt.TryReadPartitions(file, out var parts))
            {
                throw new InvalidDataException("gpt open failed");
            }
            var nodes = new List<IVfsNode>();
            foreach (var part in parts)
            {
                long bytes = (part.LastLba - part.FirstLba + 1) * VfsGpt.SectorSize;
                var slice = new SliceVirtualFile(
                    part.Name, file, part.FirstLba * VfsGpt.SectorSize, bytes);
                var dir = new YafexDirectory(file.Name, Helpers.OctalLiteral(0755));
                VfsBuilder.AddImage(dir, slice,
                    VfsImageViews.Decrypted | VfsImageViews.Expanded | VfsImageViews.Metadata,
                    keepAlive,
                    describe: () => $"partition: {part.Name}\nfirst-lba: {part.FirstLba}\n" +
                        $"last-lba: {part.LastLba}\nbytes: {bytes}\n");
                foreach (var child in dir.Tree.Values.Select(l => l.Node))
                {
                    nodes.Add(child);
                }
            }
            return nodes;
        });
    }
}

public sealed class VdfsExpander : IContentExpander
{
    public string Name => "vdfs";

    public bool Matches(IVirtualFile file)
    {
        return ExpanderSniff.Head(file, 4096, VfsVdfs.IsVolume);
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            if (!VfsVdfs.TryOpen(file, keepAlive, out var fs))
            {
                throw new InvalidDataException("vdfs open failed");
            }
            return VfsExt.BuildLazyTree(fs);
        });
    }
}
