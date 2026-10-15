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
using System.Threading.Tasks;

namespace Yafex.Gui.Services;

/// <summary>
/// Window-owned interactions the view models need (pickers, clipboard), so the
/// view models stay testable without a running UI.
/// </summary>
public interface IDialogService
{
    /// <summary>Local path of a file chosen by the user, or null when cancelled.</summary>
    Task<string?> PickFileAsync(string title);

    /// <summary>Local path of a folder chosen by the user, or null when cancelled.</summary>
    Task<string?> PickFolderAsync(string title);

    Task CopyTextAsync(string text);
}
