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

using Yafex.Fuse;

namespace Yafex.Gui.ViewModels;

/// <summary>Lazy TreeView wrapper over an <see cref="IVfsNode"/>: children
/// materialize on first expansion, mirroring the mount behavior.</summary>
public class FsNodeViewModel : ViewModelBase
{
    private readonly IVfsNode _node;
    private bool _loaded;
    private bool _expanded;

    public FsNodeViewModel(IVfsNode node)
    {
        _node = node;
        if (node.Type == VfsNodeType.Directory)
        {
            Children.Add(new FsNodeViewModel(new LoadingPlaceholder()));
        }
    }

    public string Name => _node.Name;

    public bool IsDirectory => _node.Type == VfsNodeType.Directory;

    public string Size => _node.Type == VfsNodeType.File ? _node.Size.ToString() : string.Empty;

    public IVfsNode Node => _node;

    public ObservableCollection<FsNodeViewModel> Children { get; } = new();

    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (SetProperty(ref _expanded, value) && value)
            {
                EnsureLoaded();
            }
        }
    }

    public void EnsureLoaded()
    {
        if (_loaded || _node.Type != VfsNodeType.Directory) return;
        _loaded = true;
        Children.Clear();
        try
        {
            _node.EnsureLoaded();
            foreach (var link in _node.Tree.Values.OrderBy(l => l.Node.Name))
            {
                Children.Add(new FsNodeViewModel(link.Node));
            }
        }
        catch (Exception)
        {
            // hostile subtree: show empty, like the mount does
        }
    }

    private sealed class LoadingPlaceholder : IVfsNode
    {
        public VfsNodeType Type => VfsNodeType.File;
        public string Name { get; set; } = "…";
        public int Mode { get; set; }
        public long Size => 0;
        public IDictionary<string, VfsNodeLink> Tree => new Dictionary<string, VfsNodeLink>();
        public void AddNode(IVfsNode node) { }
        public void RemoveNode(IVfsNode node) { }
        public void EnsureLoaded() { }
        public int LinkTo(string linkName) => -(int)PosixError.ENOTSUP;
    }
}
