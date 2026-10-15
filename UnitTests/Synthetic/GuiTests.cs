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
// GUI smoke tests: drive the main view-model headlessly (no windows)
// against a synthetic Askey image on a temp file.
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

using NUnit.Framework;

using Yafex.Gui.ViewModels;

namespace UnitTests;

[TestFixture]
public class GuiTests
{
    private static string WriteTemp(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".imgbin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Test]
    public void Open_BuildsTreeAndProbe()
    {
        string path = WriteTemp(BroadcomImgTests.BuildImage());
        try
        {
            using var vm = new MainViewModel();
            vm.OpenFile(path);
            StringAssert.Contains("broadcomimg", vm.ProbeLine);
            Assert.AreEqual(1, vm.Roots.Count);
            vm.Roots[0].EnsureLoaded();
            var names = vm.Roots[0].Children.Select(n => n.Name).ToList();
            CollectionAssert.Contains(names, "boot.jffs2");
            CollectionAssert.Contains(names, "root.ubi");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Select_PreviewsAndSaves()
    {
        string path = WriteTemp(BroadcomImgTests.BuildImage());
        string outPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".bin");
        try
        {
            using var vm = new MainViewModel();
            vm.OpenFile(path);
            vm.Roots[0].EnsureLoaded();
            var file = vm.Roots[0].Children.First(n => !n.IsDirectory);
            vm.Selected = file;
            Assert.IsNotEmpty(vm.HexPreview);
            Assert.IsNotEmpty(vm.InfoPreview);
            Assert.NotNull(vm.SaveSelected(outPath));
            Assert.IsTrue(new FileInfo(outPath).Length > 0);
        }
        finally
        {
            File.Delete(path);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    [Test]
    public void Open_GarbageReportsNoFormat()
    {
        var rnd = new byte[4096];
        new Random(77).NextBytes(rnd);
        string path = WriteTemp(rnd);
        try
        {
            using var vm = new MainViewModel();
            vm.OpenFile(path);
            Assert.AreEqual(0, vm.Roots.Count);
            StringAssert.Contains("No format", vm.Status);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void SaveSelected_DirectoryExportsSubtree()
    {
        string path = WriteTemp(BroadcomImgTests.BuildImage());
        string outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using var vm = new MainViewModel();
            vm.OpenFile(path);
            vm.Selected = vm.Roots[0];
            Assert.NotNull(vm.SaveSelected(outDir));
            Assert.IsTrue(File.Exists(Path.Combine(outDir, "boot.jffs2")), vm.Status);
            StringAssert.StartsWith("Exported", vm.Status);
        }
        finally
        {
            File.Delete(path);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        }
    }

    [Test]
    public void Open_ExpanderOnlyContentBrowses()
    {
        // gzip has no addon VFS: the tree comes from the expander registry
        var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            gz.Write(System.Text.Encoding.ASCII.GetBytes("hello gui"));
        }
        string path = WriteTemp(ms.ToArray());
        try
        {
            using var vm = new MainViewModel();
            vm.OpenFile(path);
            Assert.AreEqual(1, vm.Roots.Count, vm.Status);
            vm.Roots[0].EnsureLoaded();
            Assert.IsNotEmpty(vm.Roots[0].Children);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
