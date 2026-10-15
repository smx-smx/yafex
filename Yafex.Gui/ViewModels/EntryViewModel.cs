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
using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;

using Yafex.Fuse;

namespace Yafex.Gui.ViewModels;

/// <summary>A row of the file list: a folder's child, or a search hit below it.</summary>
public sealed partial class EntryViewModel : ViewModelBase
{
    /// <param name="node">The listed node.</param>
    /// <param name="baseFolder">Folder the listing (or search) ran in.</param>
    /// <param name="relativeDir">Directory of a search hit below <paramref name="baseFolder"/>, `/`-separated; empty for direct children.</param>
    public EntryViewModel(IVfsNode node, FolderViewModel baseFolder, string relativeDir = "")
    {
        Node = node;
        BaseFolder = baseFolder;
        RelativeDir = relativeDir;
        Name = node.Name;
        IsDirectory = node.Type == VfsNodeType.Directory;
        Size = IsDirectory ? 0 : node.Size;
    }

    public IVfsNode Node { get; }

    public FolderViewModel BaseFolder { get; }

    public string RelativeDir { get; }

    public string Name { get; }

    public bool IsDirectory { get; }

    /// <summary>Byte size (sort key); 0 for folders.</summary>
    public long Size { get; }

    public string SizeText => IsDirectory ? string.Empty : Format.Size(Size);

    /// <summary>Folder of a search hit, relative to where the search ran.</summary>
    public string Location => RelativeDir.Length == 0 ? "." : RelativeDir;

    public string FullPath => RelativeDir.Length == 0
        ? $"{BaseFolder.DisplayPath}/{Name}"
        : $"{BaseFolder.DisplayPath}/{RelativeDir}/{Name}";

    /// <summary>Known to hold a browsable format (explored, or detected by the preview).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlainFile))]
    public partial bool IsContainer { get; set; }

    /// <summary>A file not (yet) known to hold a browsable format.</summary>
    public bool IsPlainFile => !IsDirectory && !IsContainer;

    public string Kind
    {
        get
        {
            if (IsDirectory) return "Folder";
            var ext = Path.GetExtension(Name);
            return ext.ToLowerInvariant() switch
            {
                ".meta" => "Metadata",
                ".raw" => "Stored bytes",
                "" => "File",
                _ => $"{ext.TrimStart('.').ToUpperInvariant()} file",
            };
        }
    }
}
