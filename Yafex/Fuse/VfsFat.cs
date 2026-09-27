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

using log4net;

using DiscUtils.Fat;

namespace Yafex.Fuse;

/// <summary>
/// Shared FAT helpers for any VFS: probe an <see cref="IVirtualFile"/> for a
/// FAT12/16/32 image (strict BPB validation, not just the boot signature)
/// and open it. Tree building reuses the generic lazy builder.
/// </summary>
public static class VfsFat
{
    public static bool TryOpen(
        IVirtualFile image, ICollection<object> keepAlive, out FatFileSystem fs)
    {
        fs = null!;
        try
        {
            using var probe = image.OpenRead();
            if (probe.Length < 512) return false;
            var bpb = new byte[512];
            int off = 0;
            while (off < bpb.Length)
            {
                int n = probe.Read(bpb, off, bpb.Length - off);
                if (n <= 0) return false;
                off += n;
            }
            if (!IsBpb(bpb)) return false;

            var stream = image.OpenRead();
            var fat = new FatFileSystem(stream);
            // keep the fs (and its stream) alive for the whole mount session
            keepAlive.Add(fat);
            fs = fat;
            return true;
        }
        catch (Exception ex)
        {
            log.Debug($"fat open of '{image.Name}' failed", ex);
            return false;
        }
    }

    public static bool IsBpb(byte[] bpb)
    {
        if (bpb.Length < 512) return false;
        if (bpb[510] != 0x55 || bpb[511] != 0xAA) return false;
        int bytesPerSector = BitConverter.ToUInt16(bpb, 11);
        if (bytesPerSector != 512 && bytesPerSector != 1024
            && bytesPerSector != 2048 && bytesPerSector != 4096) return false;
        int sectorsPerCluster = bpb[13];
        if (sectorsPerCluster == 0 || (sectorsPerCluster & (sectorsPerCluster - 1)) != 0
            || sectorsPerCluster > 128) return false;
        if (BitConverter.ToUInt16(bpb, 14) == 0) return false;
        int fats = bpb[16];
        if (fats < 1 || fats > 2) return false;
        int media = bpb[21];
        if (media != 0xF0 && (media < 0xF8 || media > 0xFF)) return false;
        long sectors = BitConverter.ToUInt16(bpb, 19);
        if (sectors == 0) sectors = BitConverter.ToUInt32(bpb, 32);
        if (sectors == 0) return false;
        return true;
    }

    private static readonly ILog log = LogManager.GetLogger(typeof(VfsFat));
}
