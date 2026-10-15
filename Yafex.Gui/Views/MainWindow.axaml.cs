using System;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

using Yafex.Gui.ViewModels;

namespace Yafex.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel Model => (MainViewModel)DataContext!;

    private async void OnOpenClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open firmware image",
            AllowMultiple = false,
        });
        var file = files.FirstOrDefault();
        if (file != null)
        {
            Model.OpenFile(file.Path.LocalPath);
        }
    }

    private async void OnKeysClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load keys bundle (JSON)",
            AllowMultiple = false,
        });
        var file = files.FirstOrDefault();
        if (file != null)
        {
            Model.SetKeysFile(file.Path.LocalPath);
        }
    }

    private async void OnSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var current = Model.Selected;
        if (current == null)
        {
            return;
        }
        if (current.IsDirectory)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = $"Export '{current.Name}' into folder",
                AllowMultiple = false,
            });
            var folder = folders.FirstOrDefault();
            if (folder != null)
            {
                Model.SaveSelected(System.IO.Path.Combine(folder.Path.LocalPath, current.Name));
            }
            return;
        }
        var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save file",
            SuggestedFileName = current.Name,
        });
        if (target != null)
        {
            Model.SaveSelected(target.Path.LocalPath);
        }
    }

    private void OnCloseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Model.CloseFile();
    }
}
