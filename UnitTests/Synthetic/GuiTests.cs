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
// GUI view-model tests: drive the explorer headlessly (no windows) against
// synthetic images written to temp files.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using Yafex.Fuse;
using Yafex.Gui.Services;
using Yafex.Gui.ViewModels;

namespace UnitTests;

[TestFixture]
public class GuiTests
{
    private readonly List<string> _cleanup = new();

    [TearDown]
    public void TearDown()
    {
        foreach (var path in _cleanup)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else File.Delete(path);
            }
            catch (Exception) { }
        }
        _cleanup.Clear();
    }

    private string WriteTemp(byte[] bytes, string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"yafex-gui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _cleanup.Add(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"yafex-gui-out-{Guid.NewGuid():N}");
        _cleanup.Add(dir);
        return dir;
    }

    /// <summary>fw.tar.gz: gzip → tar with fw/{README.txt, big.bin, extra.tar.gz → tar → README.txt}.</summary>
    private string NestedImage()
    {
        var gz = new MemoryStream();
        using (var z = new GZipStream(gz, CompressionLevel.Fastest, leaveOpen: true))
        {
            z.Write(NestedExpansionTests.BuildTar());
        }
        return WriteTemp(gz.ToArray(), "fw.tar.gz");
    }

    private static EntryViewModel Entry(MainWindowViewModel vm, string name) => vm.Entries.Single(e => e.Name == name);

    /// <summary>Opens the nested image and walks down to fw/.</summary>
    private async Task<MainWindowViewModel> OpenAtFw()
    {
        var vm = new MainWindowViewModel();
        Assert.IsTrue(await vm.OpenFileAsync(NestedImage()), vm.Status);
        // the inflated tar is a file: opened as a container on demand
        await vm.ActivateCommand.ExecuteAsync(Entry(vm, "fw.tar"));
        await vm.ActivateCommand.ExecuteAsync(Entry(vm, "fw"));
        Assert.AreEqual("fw.tar.gz/fw.tar/fw", vm.CurrentFolder!.DisplayPath);
        return vm;
    }

    [Test]
    public async Task Open_BuildsTreeAndListsRoot()
    {
        using var vm = new MainWindowViewModel();
        Assert.IsTrue(await vm.OpenFileAsync(WriteTemp(BroadcomImgTests.BuildImage(), "f.imgbin")), vm.Status);
        Assert.AreEqual(1, vm.Roots.Count);
        Assert.IsTrue(vm.HasSources);
        Assert.AreSame(vm.Roots[0], vm.CurrentFolder);
        StringAssert.Contains("broadcomimg", vm.Status);
        var names = vm.Entries.Select(e => e.Name).ToList();
        CollectionAssert.Contains(names, "boot.jffs2");
        CollectionAssert.Contains(names, "root.ubi");
        // folders first
        var firstFile = vm.Entries.ToList().FindIndex(e => !e.IsDirectory);
        Assert.IsTrue(vm.Entries.Skip(firstFile).All(e => !e.IsDirectory));
    }

    [Test]
    public async Task Open_SameFileTwice_ReusesTheSource()
    {
        using var vm = new MainWindowViewModel();
        var path = NestedImage();
        await vm.OpenFileAsync(path);
        await vm.OpenFileAsync(path);
        Assert.AreEqual(1, vm.Sources.Count);
    }

    [Test]
    public async Task Open_GarbageReportsNoFormat()
    {
        var rnd = new byte[4096];
        new Random(77).NextBytes(rnd);
        using var vm = new MainWindowViewModel();
        Assert.IsFalse(await vm.OpenFileAsync(WriteTemp(rnd, "noise.bin")));
        Assert.AreEqual(0, vm.Roots.Count);
        Assert.IsFalse(vm.HasSources);
        StringAssert.Contains("no supported format", vm.Status);
    }

    [Test]
    public async Task Navigation_BreadcrumbsAndHistory()
    {
        using var vm = await OpenAtFw();
        CollectionAssert.AreEqual(new[] { "fw.tar.gz", "fw.tar", "fw" }, vm.Breadcrumbs.Select(b => b.Name));
        Assert.IsTrue(vm.BackCommand.CanExecute(null));
        Assert.IsFalse(vm.ForwardCommand.CanExecute(null));

        await vm.BackCommand.ExecuteAsync(null);
        Assert.AreEqual("fw.tar", vm.CurrentFolder!.Name);
        Assert.IsTrue(vm.ForwardCommand.CanExecute(null));
        await vm.ForwardCommand.ExecuteAsync(null);
        Assert.AreEqual("fw", vm.CurrentFolder!.Name);

        await vm.UpCommand.ExecuteAsync(null);
        Assert.AreEqual("fw.tar", vm.CurrentFolder!.Name);
        Assert.AreEqual("fw", vm.SelectedEntry?.Name, "Up selects the folder it came from");

        await vm.NavigateCommand.ExecuteAsync(vm.Breadcrumbs[0]);
        Assert.AreSame(vm.Roots[0], vm.CurrentFolder);
        Assert.IsFalse(vm.UpCommand.CanExecute(null));
    }

    [Test]
    public async Task TreeSelection_Navigates()
    {
        using var vm = await OpenAtFw();
        var tarFolder = vm.Roots[0].Children.Single(c => c.Name == "fw.tar");
        vm.SelectedTreeFolder = tarFolder;
        await Task.Delay(50);
        await tarFolder.LoadAsync();
        Assert.AreSame(tarFolder, vm.CurrentFolder);
    }

    [Test]
    public async Task Filter_MatchesSubstringAndGlob()
    {
        using var vm = await OpenAtFw();
        vm.FilterText = "readme";
        CollectionAssert.AreEqual(new[] { "README.txt" }, vm.Entries.Select(e => e.Name));
        StringAssert.Contains("match", vm.ItemSummary);
        vm.FilterText = "*.gz";
        CollectionAssert.AreEqual(new[] { "extra.tar.gz" }, vm.Entries.Select(e => e.Name));
        vm.ClearFilterCommand.Execute(null);
        Assert.Greater(vm.Entries.Count, 2);
    }

    [Test]
    public async Task Search_FindsMatchesInNestedImages()
    {
        using var vm = new MainWindowViewModel();
        await vm.OpenFileAsync(NestedImage());
        vm.SearchSubfolders = true;
        vm.FilterText = "README*";
        await vm.ApplyFilterAsync();
        Assert.IsTrue(vm.IsSearchResults);
        var hits = vm.Entries.Select(e => e.FullPath).OrderBy(p => p, StringComparer.Ordinal).ToList();
        CollectionAssert.AreEqual(new[]
        {
            "fw.tar.gz/fw.tar.d/fw/README.txt",
            "fw.tar.gz/fw.tar.d/fw/extra.tar.gz.d/extra.tar.d/README.txt",
        }, hits);

        // a hit opens in its own folder
        var nested = vm.Entries.Single(e => e.RelativeDir.Contains("extra"));
        await vm.ShowInFolderCommand.ExecuteAsync(nested);
        Assert.IsFalse(vm.IsSearchResults);
        Assert.AreEqual("extra.tar", vm.CurrentFolder!.Name);
        Assert.AreEqual("fw.tar.gz/fw.tar/fw/extra.tar.gz/extra.tar", vm.CurrentFolder!.DisplayPath);
        Assert.AreEqual("README.txt", vm.SelectedEntry?.Name);
    }

    [Test]
    public async Task Listing_HidesNestedViewsAndMarksContainersInBackground()
    {
        using var vm = await OpenAtFw();
        Assert.IsFalse(vm.Entries.Any(e => e.Name.EndsWith(".d")), "no .d folders in the listing");
        var gz = Entry(vm, "extra.tar.gz");
        var big = Entry(vm, "big.bin");
        for (int i = 0; i < 100 && !gz.IsContainer; i++) await Task.Delay(20);
        Assert.IsTrue(gz.IsContainer, "background sniff flags the gzip member");
        Assert.IsFalse(big.IsContainer);
    }

    [Test]
    public async Task Activate_ContainerFile_OpensItsContents()
    {
        using var vm = await OpenAtFw();
        var gz = Entry(vm, "extra.tar.gz");
        await vm.ActivateCommand.ExecuteAsync(gz);
        Assert.AreEqual("extra.tar.gz", vm.CurrentFolder!.Name);
        Assert.AreEqual(FolderKind.Container, vm.CurrentFolder.Kind);
        Assert.AreEqual("gzip", vm.CurrentFolder.Detail);
        CollectionAssert.AreEqual(new[] { "extra.tar" }, vm.Entries.Select(e => e.Name));
    }

    [Test]
    public async Task Explore_MountsAFileOnDemand()
    {
        using var vm = new MainWindowViewModel();
        await vm.OpenFileAsync(NestedImage());
        var tarFile = Entry(vm, "fw.tar");
        Assert.IsTrue(await vm.ExploreEntryAsync(tarFile));
        Assert.AreEqual("fw.tar", vm.CurrentFolder!.Name);
        Assert.AreEqual("tar", vm.CurrentFolder.Detail);

        await vm.ActivateCommand.ExecuteAsync(Entry(vm, "fw"));
        var big = Entry(vm, "big.bin");
        Assert.IsFalse(await vm.ExploreEntryAsync(big));
        StringAssert.Contains("no browsable format", vm.Status);
    }

    [Test]
    public async Task NestedMount_TryMount_ExpandsAFileNode()
    {
        var tar = new MemoryVirtualFile("a.tar", NestedExpansionTests.BuildTar());
        var node = new PakVfsFile(tar, Helpers.OctalLiteral(0444));
        var finder = new Yafex.Gui.Services.GuiServices().Finder;
        var tree = await Task.Run(() => NestedMount.TryMount(node, finder));
        Assert.NotNull(tree);
        Assert.AreEqual("tar", tree!.Label);
        CollectionAssert.Contains(VfsWalk.Walk(tree.Root).Select(e => e.Path).ToList(), "fw/README.txt");
        tree.Owner.Dispose();
    }

    [Test]
    public async Task Preview_ShowsTextHexInfoAndDetection()
    {
        using var vm = await OpenAtFw();
        await vm.Preview.ShowAsync(Entry(vm, "README.txt"), null);
        Assert.IsTrue(vm.Preview.IsFile);
        Assert.AreEqual("outer readme", vm.Preview.Text);
        Assert.AreEqual((int)PreviewTab.Text, vm.Preview.SelectedTab);
        Assert.AreEqual(1, vm.Preview.Hex!.Count);
        StringAssert.StartsWith("6F 75 74", vm.Preview.Hex[0].Hex);
        Assert.AreEqual("outer readme", vm.Preview.Hex[0].Ascii);
        CollectionAssert.Contains(vm.Preview.Info.Select(i => i.Key), "Size");

        var gz = Entry(vm, "extra.tar.gz");
        await vm.Preview.ShowAsync(gz, null);
        Assert.AreEqual("gzip", vm.Preview.Detected);
        Assert.AreEqual((int)PreviewTab.Hex, vm.Preview.SelectedTab);

        await vm.UpCommand.ExecuteAsync(null);
        await vm.Preview.ShowAsync(Entry(vm, "fw"), null);
        Assert.IsFalse(vm.Preview.IsFile);
        Assert.IsNull(vm.Preview.Hex);
        Assert.AreEqual((int)PreviewTab.Info, vm.Preview.SelectedTab);
    }

    [Test]
    public async Task Preview_InfoTabStaysWhenChosen()
    {
        using var vm = await OpenAtFw();
        // the user clicks away and back to Info
        vm.Preview.SelectedTab = (int)PreviewTab.Hex;
        vm.Preview.SelectedTab = (int)PreviewTab.Info;
        await vm.Preview.ShowAsync(Entry(vm, "README.txt"), null);
        Assert.AreEqual((int)PreviewTab.Info, vm.Preview.SelectedTab);
    }

    [Test]
    public async Task Export_WritesFilesAndFolders()
    {
        using var vm = await OpenAtFw();
        var dest = TempDir();
        var fw = vm.CurrentFolder!;
        int n = await vm.ExportToAsync(new[] { (fw.Node, "fw") }, dest, fw.Source);
        // own structure only: README, big.bin, extra.tar.gz (not its contents)
        Assert.AreEqual(3, n, vm.Status);
        Assert.AreEqual("outer readme", File.ReadAllText(Path.Combine(dest, "fw", "README.txt")));
        Assert.IsFalse(Directory.Exists(Path.Combine(dest, "fw", "extra.tar.gz.d")));
        StringAssert.StartsWith("Exported 3 files", vm.Status);

        // an opened container exports its contents
        await vm.ActivateCommand.ExecuteAsync(Entry(vm, "extra.tar.gz"));
        await vm.ActivateCommand.ExecuteAsync(Entry(vm, "extra.tar"));
        var inner = vm.CurrentFolder!;
        Assert.AreEqual(1, await vm.ExportToAsync(new[] { (inner.Node, inner.Name) }, dest, inner.Source));
        Assert.AreEqual("inner readme", File.ReadAllText(Path.Combine(dest, "extra.tar", "README.txt")));
    }

    [Test]
    public async Task CloseSource_ClearsViewAndHistory()
    {
        using var vm = await OpenAtFw();
        await vm.CloseSourceCommand.ExecuteAsync(vm.Roots[0]);
        Assert.AreEqual(0, vm.Roots.Count);
        Assert.IsNull(vm.CurrentFolder);
        Assert.IsEmpty(vm.Entries);
        Assert.IsFalse(vm.BackCommand.CanExecute(null));
    }

    [Test]
    public void HexDocument_FormatsRowsAndShortTail()
    {
        var data = Enumerable.Range(0, 20).Select(i => (byte)(0x41 + i)).ToArray();
        using var hex = new HexDocument(() => new MemoryStream(data), data.Length);
        Assert.AreEqual(2, hex.Count);
        Assert.AreEqual("00000000", hex[0].Offset);
        Assert.AreEqual("41 42 43 44 45 46 47 48  49 4A 4B 4C 4D 4E 4F 50 ", hex[0].Hex);
        Assert.AreEqual("ABCDEFGHIJKLMNOP", hex[0].Ascii);
        Assert.AreEqual("00000010", hex[1].Offset);
        Assert.AreEqual("QRST", hex[1].Ascii);

        using var narrow = new HexDocument(() => new MemoryStream(data), data.Length, 8);
        Assert.AreEqual(3, narrow.Count);
        Assert.AreEqual("00000008", narrow[1].Offset);
        Assert.AreEqual("IJKLMNOP", narrow[1].Ascii);
    }

    [TestCase(0L, "0 B")]
    [TestCase(1023L, "1023 B")]
    [TestCase(1536L, "1.5 KiB")]
    [TestCase(300000L, "293 KiB")]
    [TestCase(5L << 30, "5.0 GiB")]
    public void Format_Size(long bytes, string expected) => Assert.AreEqual(expected, Format.Size(bytes));

    [TestCase("read", "README.txt", true)]
    [TestCase("*.txt", "README.txt", true)]
    [TestCase("*.txt", "README.txt.bak", false)]
    [TestCase("r?adme*", "README.txt", true)]
    [TestCase("", "anything", true)]
    public void Format_NameMatcher(string pattern, string name, bool expected)
    {
        Assert.AreEqual(expected, Format.NameMatcher(pattern)(name));
    }

    /// <summary>A source whose root holds boot.pak with .raw and .meta views.</summary>
    private static SourceViewModel ImageWithViews()
    {
        var root = new YafexDirectory("img", Helpers.OctalLiteral(0755));
        VfsBuilder.AddImage(root, new TextVirtualFile("boot.pak", "decrypted text"),
            VfsImageViews.Decrypted | VfsImageViews.Raw | VfsImageViews.Metadata, new List<object>(),
            raw: new TextVirtualFile("x", "\u0001\u0002ENCRYPTED"),
            describe: () => "offset: 0x17C\nsize: 14\nsigned image");
        return new SourceViewModel("/tmp/img", new NestedTree(root, "test", new MemoryStream()), new[] { "test" });
    }

    [Test]
    public async Task FileViews_FoldIntoTheirEntry()
    {
        using var vm = new MainWindowViewModel();
        using var source = ImageWithViews();
        await vm.GoToAsync(source.Root);
        var entry = vm.Entries.Single();
        Assert.AreEqual("boot.pak", entry.Name);
        Assert.IsTrue(entry.HasStored);
        Assert.AreEqual("boot.pak.meta", entry.MetadataView?.Name);
    }

    [Test]
    public async Task Preview_ShowsMetadataRowsAndStoredBytes()
    {
        using var vm = new MainWindowViewModel();
        using var source = ImageWithViews();
        await vm.GoToAsync(source.Root);
        var entry = vm.Entries.Single();
        await vm.Preview.ShowAsync(entry, null);
        Assert.AreEqual("0x17C", vm.Preview.Info.Single(r => r.Key == "Offset").Value);
        Assert.AreEqual("signed image", vm.Preview.Info.Single(r => r.Key == "Metadata").Value);
        Assert.IsTrue(vm.Preview.Info.Any(r => r.Key == "Stored size"));
        Assert.IsTrue(vm.Preview.HasStored);
        Assert.AreEqual("decrypted text", vm.Preview.Text);

        vm.Preview.ShowStored = true;
        for (int i = 0; i < 100 && !vm.Preview.Text.Contains("ENCRYPTED"); i++) await Task.Delay(10);
        StringAssert.Contains("ENCRYPTED", vm.Preview.Text);
        Assert.AreEqual("01", vm.Preview.Hex![0].Hex[..2]);

        vm.Preview.ShowStored = false;
        for (int i = 0; i < 100 && vm.Preview.Text != "decrypted text"; i++) await Task.Delay(10);
        Assert.AreEqual("decrypted text", vm.Preview.Text);
    }

    [Test]
    public async Task Export_IncludesViewsOnRequest()
    {
        using var vm = new MainWindowViewModel();
        using var source = ImageWithViews();
        await vm.GoToAsync(source.Root);
        var dest = TempDir();
        Assert.AreEqual(1, await vm.ExportToAsync(new[] { (source.Root.Node, "img") }, dest, source));
        Assert.IsFalse(File.Exists(Path.Combine(dest, "img", "boot.pak.raw")));
        var dest2 = TempDir();
        Assert.AreEqual(3, await vm.ExportToAsync(new[] { (source.Root.Node, "img") }, dest2, source, includeViews: true));
        StringAssert.Contains("offset: 0x17C", File.ReadAllText(Path.Combine(dest2, "img", "boot.pak.meta")));
    }
}
