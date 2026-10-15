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
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

using Yafex.Fuse;

namespace Yafex.Gui.ViewModels;

public enum FolderKind
{
    /// <summary>An opened image (tree root).</summary>
    Source,
    Folder,
    /// <summary>A file explored as a container.</summary>
    Container,
}

/// <summary>
/// A directory in the navigation tree. Children load off the UI thread on first
/// expansion or navigation (lazy mounts may decrypt/inflate to list), and are
/// shared by the tree and the file list.
/// </summary>
public sealed partial class FolderViewModel : ViewModelBase
{
    private Task<IReadOnlyList<IVfsNode>>? _load;

    public FolderViewModel(IVfsNode node, string name, FolderViewModel? parent, SourceViewModel source,
        FolderKind kind = FolderKind.Folder, string? detail = null)
    {
        Node = node;
        Name = name;
        Parent = parent;
        Source = source;
        Kind = kind;
        Detail = detail;
        Children.Add(new FolderViewModel());
    }

    /// <summary>"Loading…" child that gives unloaded folders an expander arrow.</summary>
    private FolderViewModel()
    {
        Node = null!;
        Source = null!;
        Name = "Loading…";
        IsPlaceholder = true;
    }

    public IVfsNode Node { get; }

    public string Name { get; }

    public FolderViewModel? Parent { get; }

    public SourceViewModel Source { get; }

    public FolderKind Kind { get; }

    /// <summary>What a source/container was recognized as (format name).</summary>
    public string? Detail { get; }

    public bool IsPlaceholder { get; }

    public bool IsContainer => Kind != FolderKind.Folder;

    public ObservableCollection<FolderViewModel> Children { get; } = new();

    /// <summary>All loaded child nodes (folders and files), ordinal by name.</summary>
    public IReadOnlyList<IVfsNode> Items { get; private set; } = Array.Empty<IVfsNode>();

    /// <summary>
    /// Nested-content views of this folder's files, by file name. Not listed:
    /// a file is opened as a container on demand (sniffed only when asked).
    /// </summary>
    public IReadOnlyDictionary<string, NestedVfsDirectory> NestedViews { get; private set; }
        = new Dictionary<string, NestedVfsDirectory>();

    /// <summary>`.raw`/`.meta` views of this folder's files, folded into their entries.</summary>
    public IReadOnlyList<ImageViewFile> FileViews { get; private set; } = Array.Empty<ImageViewFile>();

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>Why the folder could not be expanded (it then shows empty).</summary>
    [ObservableProperty]
    public partial string? LoadError { get; private set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !IsPlaceholder)
        {
            _ = LoadAsync();
        }
    }

    /// <summary>`image.bin/rootfs/etc` from the opened image down.</summary>
    public string DisplayPath => Parent == null ? Name : $"{Parent.DisplayPath}/{Name}";

    /// <summary>This folder and its parents, root first.</summary>
    public IReadOnlyList<FolderViewModel> PathFromRoot
    {
        get
        {
            var chain = new List<FolderViewModel>();
            for (var f = this; f != null; f = f.Parent) chain.Add(f);
            chain.Reverse();
            return chain;
        }
    }

    /// <summary>Loads children once; later calls share the same task.</summary>
    public Task<IReadOnlyList<IVfsNode>> LoadAsync() => _load ??= LoadCoreAsync();

    private async Task<IReadOnlyList<IVfsNode>> LoadCoreAsync()
    {
        IsLoading = true;
        var (items, nested, fileViews, error) = await Task.Run(() =>
        {
            var none = (IReadOnlyList<NestedVfsDirectory>)Array.Empty<NestedVfsDirectory>();
            var noViews = (IReadOnlyList<ImageViewFile>)Array.Empty<ImageViewFile>();
            using var lease = Source.Lease();
            if (lease == null)
            {
                return ((IReadOnlyList<IVfsNode>)Array.Empty<IVfsNode>(), none, noViews, "image closed");
            }
            try
            {
                var children = VfsWalk.Children(Node);
                return (children, VfsWalk.NestedViewsOf(Node), VfsWalk.FileViewsOf(Node),
                    (Node as LazyVfsDirectory)?.LoadError?.Message);
            }
            catch (Exception ex)
            {
                return ((IReadOnlyList<IVfsNode>)Array.Empty<IVfsNode>(), none, noViews, ex.Message);
            }
        });
        Items = items;
        FileViews = fileViews;
        var views = new Dictionary<string, NestedVfsDirectory>();
        foreach (var view in nested)
        {
            views.TryAdd(view.Image.Name, view);
        }
        NestedViews = views;
        LoadError = error;
        var explored = Children.Where(c => c.Kind == FolderKind.Container).ToList();
        Children.Clear();
        foreach (var node in items.Where(n => n.Type == VfsNodeType.Directory))
        {
            Children.Add(new FolderViewModel(node, node.Name, this, Source));
        }
        foreach (var container in explored)
        {
            Children.Add(container);
        }
        IsLoading = false;
        return items;
    }

    /// <summary>Tree child for a loaded directory node.</summary>
    public FolderViewModel? ChildFor(IVfsNode node)
    {
        return Children.FirstOrDefault(c => !c.IsPlaceholder && c.Kind == FolderKind.Folder && ReferenceEquals(c.Node, node));
    }

    /// <summary>Tree child for a file of this folder already explored as a container.</summary>
    public FolderViewModel? ContainerFor(string fileName)
    {
        return Children.FirstOrDefault(c => c.Kind == FolderKind.Container && c.Name == fileName);
    }

    /// <summary>Adds the tree found inside one of this folder's files.</summary>
    public FolderViewModel AddContainer(IVfsNode root, string fileName, string label)
    {
        var existing = ContainerFor(fileName);
        if (existing != null) return existing;
        var container = new FolderViewModel(root, fileName, this, Source, FolderKind.Container, label);
        Children.Add(container);
        return container;
    }

    public override string ToString() => DisplayPath;
}
