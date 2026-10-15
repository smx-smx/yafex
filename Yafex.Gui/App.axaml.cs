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

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Yafex.Gui.ViewModels;
using Yafex.Gui.Views;

namespace Yafex.Gui;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var model = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = model,
            };
            // `yafex-gui <image>...` opens the given images
            foreach (var path in desktop.Args?.Where(a => !a.StartsWith('-')) ?? Enumerable.Empty<string>())
            {
                _ = model.OpenFileAsync(path);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
