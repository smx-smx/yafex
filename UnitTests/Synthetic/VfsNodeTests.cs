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
// Synthetic VFS tests: node passthrough for every addon plus in-memory
// tree walks. No mount, no disk.
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.EpkV1;
using Yafex.FileFormats.EpkV2;
using Yafex.FileFormats.EpkV3;
using Yafex.FileFormats.FreescaleNand;
using Yafex.FileFormats.LgUp;
using Yafex.FileFormats.LxBoot;
using Yafex.FileFormats.Lzhs;
using Yafex.FileFormats.LzhsFs;
using Yafex.FileFormats.MediatekPkg;
using Yafex.FileFormats.MStarPkg;
using Yafex.FileFormats.Nfwb;
using Yafex.FileFormats.Partinfo;
using Yafex.FileFormats.SddlSec;
using Yafex.FileFormats.Xex;
using Yafex.Fuse;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class VfsNodeTests
{
    [Test]
    public void AllAddons_ReturnNonNull()
    {
        var ds = Mem(Zeros(0x4000), "vfs-probe");
        var keys = EmptyKeys();
        var addons = new IFormatAddon[]
        {
            new Epk1Addon(),
            new Epk2Addon(keys),
            new Epk2BetaAddon(keys),
            new Epk3NewAddon(keys),
            new PartinfoAddon(),
            new LzhsAddon(),
            new MStarPkgAddon(),
            new FreescaleNandAddon(),
            new XexAddon(),
            new LxSecureBootAddon(),
            new NfwbAddon(),
            new MediatekPkgAddon(keys),
            new LzhsFsAddon(),
            new SddlSecAddon(keys),
            new LgUpAddon(keys),
        };
        foreach (var addon in addons)
        {
            Assert.DoesNotThrow(() =>
            {
                var node = addon.CreateVfsNode(ds);
                Assert.NotNull(node);
            }, $"CreateVfsNode failed for {addon.GetType().Name}");
        }
    }

    [Test]
    public void YafexVfs_AddFileAndWalk()
    {
        var vfs = new YafexVfs();
        var ds = Mem(Encoding.ASCII.GetBytes("payload"), "hello.bin");
        vfs.AddDataSource(ds);
        vfs.Root.EnsureLoaded();
        Assert.That(vfs.Root.Tree.Keys, Does.Contain("hello.bin"));
        var node = vfs.Root.Tree["hello.bin"].Node;
        Assert.AreEqual(7, node.Size);
    }

    [Test]
    public void LazyDirectory_LoadsOnceInMemory()
    {
        var calls = 0;
        var dir = new LazyVfsDirectory("lazy", Helpers.OctalLiteral(0755), () =>
        {
            calls++;
            return new IVfsNode[] { new YafexDirectory("child", Helpers.OctalLiteral(0755)) };
        });
        dir.EnsureLoaded();
        dir.EnsureLoaded();
        Assert.AreEqual(1, calls);
        Assert.That(dir.Tree.Keys, Does.Contain("child"));
    }
}
