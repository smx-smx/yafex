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
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

using Yafex.Gui.Services;

namespace Yafex.Gui.Views;

/// <summary><see cref="IDialogService"/> backed by a window's storage provider and clipboard.</summary>
public sealed class DialogService : IDialogService
{
    private readonly TopLevel _owner;

    public DialogService(TopLevel owner)
    {
        _owner = owner;
    }

    public async Task<string?> PickFileAsync(string title)
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task CopyTextAsync(string text)
    {
        if (_owner.Clipboard != null)
        {
            await _owner.Clipboard.SetTextAsync(text);
        }
    }
}
