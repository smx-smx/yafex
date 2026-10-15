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
using System.ComponentModel;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

using Yafex.Gui.ViewModels;

namespace Yafex.Gui.Views;

public partial class MainWindow : Window
{
    /// <summary>Index of the search-only Location column in <c>EntryGrid</c>.</summary>
    private const int LocationColumnIndex = 1;

    private MainWindowViewModel? _model;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        EntryGrid.DoubleTapped += OnGridDoubleTapped;
        EntryGrid.AddHandler(KeyDownEvent, OnGridKeyDown, RoutingStrategies.Tunnel);
        HexList.SizeChanged += (_, e) => FitHex(e.NewSize.Width);
    }

    /// <summary>Monospace cell width at the hex font size (0.6 em at 12 px).</summary>
    private const double HexCharWidth = 7.3;

    private void FitHex(double width)
    {
        // row padding (16) + scrollbar (14) + two 12 px column gaps
        _model?.Preview.FitHex((int)((width - 16 - 14 - 24) / HexCharWidth));
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_model != null) _model.PropertyChanged -= OnModelPropertyChanged;
        _model = DataContext as MainWindowViewModel;
        if (_model != null)
        {
            _model.Dialogs = new DialogService(this);
            _model.PropertyChanged += OnModelPropertyChanged;
        }
        base.OnDataContextChanged(e);
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.IsSearchResults):
                // DataGrid columns are not in the visual tree: toggle in code
                EntryGrid.Columns[LocationColumnIndex].IsVisible = _model!.IsSearchResults;
                break;
            case nameof(MainWindowViewModel.CurrentFolder):
                CrumbScroller.ScrollToEnd();
                break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _model?.Dispose();
        base.OnClosed(e);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model == null) return;
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            FilterBox.Focus();
            FilterBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_model.IsBusy)
            {
                _model.CancelCommand.Execute(null);
            }
            else if (_model.FilterText.Length > 0)
            {
                _model.ClearFilterCommand.Execute(null);
                EntryGrid.Focus();
            }
            else
            {
                return;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Down && FilterBox.IsFocused && _model.Entries.Count > 0)
        {
            // from the filter box straight into the results
            _model.SelectedEntry ??= _model.Entries[0];
            EntryGrid.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && FilterBox.IsFocused && _model.Entries.Count == 1)
        {
            _model.ActivateCommand.Execute(_model.Entries[0]);
            e.Handled = true;
        }
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model == null) return;
        switch (e.Key)
        {
            case Key.Enter:
                _model.ActivateCommand.Execute(_model.SelectedEntry);
                e.Handled = true;
                break;
            case Key.Back:
                _model.UpCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        // ignore double-clicks on headers/empty space
        if (e.Source is Control c && c.FindAncestorOfType<DataGridRow>(includeSelf: true) != null)
        {
            _model?.ActivateCommand.Execute(_model.SelectedEntry);
        }
    }

    private void OnCrumbClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FolderViewModel folder)
        {
            _model?.NavigateCommand.Execute(folder);
        }
    }

    private void OnOpenEntry(object? sender, RoutedEventArgs e) => _model?.ActivateCommand.Execute(_model.SelectedEntry);

    private void OnExportSelection(object? sender, RoutedEventArgs e)
    {
        _model?.ExportCommand.Execute(EntryGrid.SelectedItems);
    }

    private void OnExportTreeFolder(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FolderViewModel folder)
        {
            _model?.ExportFolderCommand.Execute(folder);
        }
    }

    private void OnCloseTreeSource(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FolderViewModel folder)
        {
            _model?.CloseSourceCommand.Execute(folder);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (_model == null) return;
        var files = e.DataTransfer.TryGetFiles();
        if (files == null) return;
        foreach (var item in files.OfType<IStorageFile>())
        {
            var path = item.TryGetLocalPath();
            if (path != null)
            {
                await _model.OpenFileAsync(path);
            }
        }
    }
}
