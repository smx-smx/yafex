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
// Synthetic ext4 probe tests: magic accept/reject and graceful failure on
// truncated or corrupt images. Fully in-memory; real filesystems are proven
// via live mounts.
using System.Collections.Generic;

using NUnit.Framework;

using Yafex.Fuse;

namespace UnitTests;

[TestFixture]
public class VfsExtTests
{
    private static MemoryVirtualFile Image(byte[] content) =>
        new("img.bin", content);

    [Test]
    public void Probe_RejectsWrongMagic()
    {
        var buf = new byte[2048];
        buf[1080] = 0x51; buf[1081] = 0x41;
        Assert.IsFalse(VfsExt.TryOpen(Image(buf), new List<object>(), out _));
    }

    [Test]
    public void Probe_RejectsTruncatedImage()
    {
        Assert.IsFalse(VfsExt.TryOpen(Image(new byte[100]), new List<object>(), out _));
    }

    [Test]
    public void Probe_RejectsEmptyImage()
    {
        Assert.IsFalse(VfsExt.TryOpen(Image(new byte[4096]), new List<object>(), out _));
    }

    [Test]
    public void Probe_FailsGracefullyOnCorruptSuperblock()
    {
        // right magic, garbage superblock: open must fail, not throw
        var buf = new byte[4096];
        buf[1080] = 0x53; buf[1081] = 0xEF;
        Assert.IsFalse(VfsExt.TryOpen(Image(buf), new List<object>(), out _));
    }
}
