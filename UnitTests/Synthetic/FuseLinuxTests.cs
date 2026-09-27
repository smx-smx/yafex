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
// Linux libfuse3 ABI layout tests: pure marshaling checks, no kernel,
// no mount, run on any platform.
using System.Runtime.InteropServices;

using NUnit.Framework;

using Yafex.Fuse;

namespace UnitTests;

[TestFixture]
public class FuseLinuxTests
{
    [Test]
    public void LinuxStat_MatchesGlibcX64Layout()
    {
        Assert.AreEqual(144, Marshal.SizeOf<LinuxStat>());
        Assert.AreEqual(0, (int)Marshal.OffsetOf<LinuxStat>(nameof(LinuxStat.st_dev)));
        Assert.AreEqual(8, (int)Marshal.OffsetOf<LinuxStat>(nameof(LinuxStat.st_ino)));
        Assert.AreEqual(16, (int)Marshal.OffsetOf<LinuxStat>(nameof(LinuxStat.st_nlink)));
        Assert.AreEqual(24, (int)Marshal.OffsetOf<LinuxStat>(nameof(LinuxStat.st_mode)));
        Assert.AreEqual(48, (int)Marshal.OffsetOf<LinuxStat>(nameof(LinuxStat.st_size)));
        Assert.AreEqual(72, (int)Marshal.OffsetOf<LinuxStat>(nameof(LinuxStat.st_atim_sec)));
    }

    [Test]
    public void LinuxOps_HasFuse3FieldOrder()
    {
        // 42 slots x 8 bytes; getattr first (fuse3 header order)
        Assert.AreEqual(42 * 8, Marshal.SizeOf<LinuxFuseOperations>());
        Assert.AreEqual(0, (int)Marshal.OffsetOf<LinuxFuseOperations>(nameof(LinuxFuseOperations.getattr)));
        Assert.AreEqual(12 * 8, (int)Marshal.OffsetOf<LinuxFuseOperations>(nameof(LinuxFuseOperations.open)));
        Assert.AreEqual(13 * 8, (int)Marshal.OffsetOf<LinuxFuseOperations>(nameof(LinuxFuseOperations.read)));
        Assert.AreEqual(23 * 8, (int)Marshal.OffsetOf<LinuxFuseOperations>(nameof(LinuxFuseOperations.opendir)));
        Assert.AreEqual(24 * 8, (int)Marshal.OffsetOf<LinuxFuseOperations>(nameof(LinuxFuseOperations.readdir)));
        Assert.AreEqual(41 * 8, (int)Marshal.OffsetOf<LinuxFuseOperations>(nameof(LinuxFuseOperations.lseek)));
    }
}
