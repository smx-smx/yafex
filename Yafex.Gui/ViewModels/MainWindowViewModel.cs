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
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Yafex.Fuse;
using Yafex.Gui.Services;

namespace Yafex.Gui.ViewModels;

/// <summary>
/// The explorer: opened images in a lazy folder tree, the current folder's
/// listing (or recursive search hits), breadcrumbs with back/forward/up
/// history, the preview pane, and container exploring/export as background
/// operations with progress and cancel.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    /// <summary>Search stops collecting after this many hits.</summary>
    public const int MaxSearchResults = 5000;

    private readonly GuiServices _services = new();
    private readonly Stack<FolderViewModel> _back = new();
    private readonly Stack<FolderViewModel> _forward = new();
    private List<EntryViewModel> _folderEntries = new();
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _operationCts;
    private bool _disposed;

    public MainWindowViewModel(IDialogService? dialogs = null)
    {
        Dialogs = dialogs;
    }

    public IDialogService? Dialogs { get; set; }

    public ObservableCollection<SourceViewModel> Sources { get; } = new();

    /// <summary>Tree roots: one per opened image.</summary>
    public ObservableCollection<FolderViewModel> Roots { get; } = new();

    public ObservableCollection<FolderViewModel> Breadcrumbs { get; } = new();

    public PreviewViewModel Preview { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyListing))]
    public partial ObservableCollection<EntryViewModel> Entries { get; private set; } = new();

    partial void OnEntriesChanged(ObservableCollection<EntryViewModel> oldValue, ObservableCollection<EntryViewModel> newValue)
    {
        if (oldValue != null) oldValue.CollectionChanged -= OnEntriesCollectionChanged;
        newValue.CollectionChanged += OnEntriesCollectionChanged;
    }

    private void OnEntriesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsEmptyListing));
    }

    /// <summary>A folder (or search) is shown and has nothing to list.</summary>
    public bool IsEmptyListing => CurrentFolder != null && Entries.Count == 0 && !IsBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpCommand))]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(IsEmptyListing))]
    public partial FolderViewModel? CurrentFolder { get; private set; }

    /// <summary>Tree selection; selecting a folder navigates to it.</summary>
    [ObservableProperty]
    public partial FolderViewModel? SelectedTreeFolder { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExploreCommand))]
    public partial EntryViewModel? SelectedEntry { get; set; }

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    /// <summary>Filter searches the whole subtree instead of the current folder.</summary>
    [ObservableProperty]
    public partial bool SearchSubfolders { get; set; }

    /// <summary>The list shows search hits (with their location) instead of a folder.</summary>
    [ObservableProperty]
    public partial bool IsSearchResults { get; private set; }

    [ObservableProperty]
    public partial string Status { get; private set; } = "Open a firmware image to begin (Ctrl+O, or drop a file here).";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(IsEmptyListing))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string? BusyText { get; private set; }

    /// <summary>`42 items · 3 folders · 1.2 MiB` for the current listing.</summary>
    [ObservableProperty]
    public partial string ItemSummary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? KeysFile { get; private set; }

    public bool HasSources => Sources.Count > 0;

    public string Title => CurrentFolder == null ? "Yafex" : $"{CurrentFolder.Name} — Yafex";

    partial void OnSelectedTreeFolderChanged(FolderViewModel? value)
    {
        if (value != null && !value.IsPlaceholder && value != CurrentFolder)
        {
            _ = GoToAsync(value);
        }
    }

    partial void OnSelectedEntryChanged(EntryViewModel? value)
    {
        _ = Preview.ShowAsync(value, _services.Finder);
    }

    private bool _suppressFilter;

    partial void OnFilterTextChanged(string value)
    {
        if (!_suppressFilter) _ = ApplyFilterAsync();
    }

    partial void OnSearchSubfoldersChanged(bool value) => _ = ApplyFilterAsync();

    // ---- opening -------------------------------------------------------

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (Dialogs == null) return;
        var path = await Dialogs.PickFileAsync("Open firmware image");
        if (path != null)
        {
            await OpenFileAsync(path);
        }
    }

    /// <summary>Opens <paramref name="path"/> as a new tree root and shows it.</summary>
    public async Task<bool> OpenFileAsync(string path)
    {
        var full = Path.GetFullPath(path);
        var already = Sources.FirstOrDefault(s => s.Path == full);
        if (already != null)
        {
            await GoToAsync(already.Root);
            return true;
        }
        var name = Path.GetFileName(full);
        using var busy = BeginBusy($"Opening {name}…");
        try
        {
            var finder = _services.Finder;
            var (tree, matches) = await Task.Run(() => NestedMount.OpenFile(full, finder));
            if (tree == null)
            {
                Status = matches.Count == 0
                    ? $"{name}: no supported format found."
                    : $"{name}: detected {string.Join(", ", matches)}, but it cannot be browsed.";
                return false;
            }
            var source = new SourceViewModel(full, tree, matches);
            Sources.Add(source);
            Roots.Add(source.Root);
            OnPropertyChanged(nameof(HasSources));
            source.Root.IsExpanded = true;
            await GoToAsync(source.Root);
            Status = matches.Count > 1
                ? $"Opened {name} as {source.Format} (also matched: {string.Join(", ", matches.Skip(1))})."
                : $"Opened {name} as {source.Format}.";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Could not open {name}: {ex.Message}";
            return false;
        }
    }

    [RelayCommand]
    private async Task CloseSourceAsync(FolderViewModel? folder)
    {
        var source = (folder ?? CurrentFolder)?.Source;
        if (source == null) return;
        bool showingIt = CurrentFolder?.Source == source;
        Prune(_back, source);
        Prune(_forward, source);
        if (Preview.Entry?.BaseFolder.Source == source)
        {
            Preview.Reset();
        }
        Roots.Remove(source.Root);
        Sources.Remove(source);
        OnPropertyChanged(nameof(HasSources));
        if (showingIt)
        {
            SetCurrent(null);
            if (Roots.Count > 0)
            {
                await GoToAsync(Roots[^1], record: false);
            }
        }
        source.Dispose();
        NotifyHistory();
        Status = $"Closed {source.FileName}.";
    }

    private static void Prune(Stack<FolderViewModel> stack, SourceViewModel source)
    {
        var keep = stack.Where(f => f.Source != source).Reverse().ToList();
        stack.Clear();
        foreach (var f in keep) stack.Push(f);
    }

    [RelayCommand]
    private async Task LoadKeysAsync()
    {
        if (Dialogs == null) return;
        var path = await Dialogs.PickFileAsync("Load keys bundle (JSON)");
        if (path != null)
        {
            SetKeysFile(path);
        }
    }

    /// <summary>Uses the key bundle at <paramref name="path"/> for images opened from now on.</summary>
    public void SetKeysFile(string? path)
    {
        var error = _services.Configure(path);
        KeysFile = error == null ? path : null;
        Status = error != null ? $"Keys not loaded: {error}"
            : path == null ? "Keys cleared." : $"Keys loaded from {path}; reopen encrypted images to use them.";
    }

    // ---- navigation ----------------------------------------------------

    [RelayCommand]
    private Task NavigateAsync(FolderViewModel? folder) => folder == null ? Task.CompletedTask : GoToAsync(folder);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private Task BackAsync()
    {
        if (CurrentFolder != null) _forward.Push(CurrentFolder);
        return GoToAsync(_back.Pop(), record: false);
    }

    private bool CanGoBack() => _back.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private Task ForwardAsync()
    {
        if (CurrentFolder != null) _back.Push(CurrentFolder);
        return GoToAsync(_forward.Pop(), record: false);
    }

    private bool CanGoForward() => _forward.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private async Task UpAsync()
    {
        var from = CurrentFolder;
        if (from?.Parent == null) return;
        await GoToAsync(from.Parent);
        // land on the folder (or container file) we came from
        SelectedEntry = Entries.FirstOrDefault(e => e.Name == from.Name);
    }

    private bool CanGoUp() => CurrentFolder?.Parent != null;

    /// <summary>Shows <paramref name="folder"/> in the list, tree and breadcrumbs.</summary>
    public async Task GoToAsync(FolderViewModel folder, bool record = true)
    {
        if (folder == CurrentFolder) return;
        if (record && CurrentFolder != null)
        {
            _back.Push(CurrentFolder);
            _forward.Clear();
        }
        SetCurrent(folder);
        NotifyHistory();
        await LoadEntriesAsync(folder);
    }

    private void SetCurrent(FolderViewModel? folder)
    {
        CancelSearch();
        CurrentFolder = folder;
        Breadcrumbs.Clear();
        if (folder != null)
        {
            foreach (var f in folder.PathFromRoot)
            {
                Breadcrumbs.Add(f);
                if (f != folder) f.IsExpanded = true;
            }
        }
        SelectedTreeFolder = folder;
        _folderEntries = new();
        // a new folder starts unfiltered, without kicking off a filter pass
        _suppressFilter = true;
        FilterText = string.Empty;
        _suppressFilter = false;
        IsSearchResults = false;
        Entries = new();
        SelectedEntry = null;
        ItemSummary = string.Empty;
    }

    private void NotifyHistory()
    {
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
        UpCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadEntriesAsync(FolderViewModel folder)
    {
        IReadOnlyList<IVfsNode> items;
        using (BeginBusy($"Loading {folder.Name}…"))
        {
            items = await folder.LoadAsync();
        }
        if (CurrentFolder != folder) return;
        _folderEntries = items
            .Select(n => new EntryViewModel(n, folder) { IsContainer = n.Type != VfsNodeType.Directory && HasContents(folder, n.Name) })
            .OrderBy(e => e.IsDirectory ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ShowFolderEntries();
        if (folder.LoadError != null)
        {
            Status = $"Could not expand {folder.Name}: {folder.LoadError}";
        }
    }

    // ---- filter & search ------------------------------------------------

    private void ShowFolderEntries()
    {
        var match = Format.NameMatcher(FilterText);
        var shown = _folderEntries.Where(e => match(e.Name)).ToList();
        IsSearchResults = false;
        Entries = new ObservableCollection<EntryViewModel>(shown);
        Summarize(FilterText.Trim().Length > 0 ? $"{shown.Count} of {_folderEntries.Count} items match" : null);
    }

    private void Summarize(string? prefix)
    {
        int folders = Entries.Count(e => e.IsDirectory);
        long bytes = Entries.Sum(e => e.Size);
        var parts = new List<string> { prefix ?? $"{Entries.Count} items" };
        if (folders > 0) parts.Add($"{folders} folders");
        if (bytes > 0) parts.Add(Format.Size(bytes));
        ItemSummary = string.Join(" · ", parts);
    }

    /// <summary>
    /// Re-applies the filter box: filters the current folder, or (with
    /// <see cref="SearchSubfolders"/>) searches its subtree after a short debounce.
    /// </summary>
    public async Task ApplyFilterAsync()
    {
        CancelSearch();
        var folder = CurrentFolder;
        if (folder == null) return;
        if (!SearchSubfolders || FilterText.Trim().Length == 0)
        {
            ShowFolderEntries();
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        try
        {
            await Task.Delay(250, cts.Token); // debounce typing
        }
        catch (OperationCanceledException)
        {
            return;
        }
        await SearchAsync(folder, FilterText, cts);
    }

    private async Task SearchAsync(FolderViewModel folder, string pattern, CancellationTokenSource cts)
    {
        var match = Format.NameMatcher(pattern);
        var hits = new ConcurrentQueue<VfsEntry>();
        var results = new ObservableCollection<EntryViewModel>();
        Entries = results;
        IsSearchResults = true;
        SelectedEntry = null;
        int found = 0;
        var walk = Task.Run(() =>
        {
            using var lease = folder.Source.Lease();
            if (lease == null) return;
            foreach (var e in VfsWalk.Walk(folder.Node))
            {
                if (cts.IsCancellationRequested) return;
                if (!match(e.Node.Name)) continue;
                hits.Enqueue(e);
                if (Interlocked.Increment(ref found) >= MaxSearchResults) return;
            }
        });
        using (BeginBusy($"Searching {folder.Name} for “{pattern}”…"))
        {
            // drain on this (UI) thread so the bound collection is only touched here
            while (true)
            {
                bool done = walk.IsCompleted;
                while (hits.TryDequeue(out var e))
                {
                    if (cts.IsCancellationRequested) break;
                    var slash = e.Path.LastIndexOf('/');
                    results.Add(new EntryViewModel(e.Node, folder, slash < 0 ? string.Empty : e.Path[..slash]));
                }
                if (done || cts.IsCancellationRequested) break;
                BusyText = $"Searching {folder.Name}… {results.Count} found";
                await Task.WhenAny(walk, Task.Delay(150));
            }
        }
        if (cts.IsCancellationRequested) return;
        if (walk.IsFaulted)
        {
            Status = $"Search failed: {walk.Exception?.GetBaseException().Message}";
        }
        Summarize(results.Count >= MaxSearchResults
            ? $"First {MaxSearchResults} matches"
            : $"{results.Count} matches in {folder.Name}");
    }

    private void CancelSearch()
    {
        _searchCts?.Cancel();
        _searchCts = null;
    }

    [RelayCommand]
    private void ClearFilter() => FilterText = string.Empty;

    // ---- entries ---------------------------------------------------------

    /// <summary>Double-click / Enter: open folders, explore files as containers.</summary>
    [RelayCommand]
    private async Task ActivateAsync(EntryViewModel? entry)
    {
        entry ??= SelectedEntry;
        if (entry == null) return;
        if (!entry.IsDirectory)
        {
            if (!await ExploreEntryAsync(entry))
            {
                Preview.SelectedTab = (int)PreviewTab.Hex;
            }
            return;
        }
        var parent = await FolderOfAsync(entry);
        if (parent == null) return;
        await parent.LoadAsync();
        var target = parent.ChildFor(entry.Node);
        if (target != null)
        {
            await GoToAsync(target);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExplore))]
    private Task ExploreAsync(EntryViewModel? entry) => ExploreEntryAsync(entry ?? SelectedEntry);

    private bool CanExplore(EntryViewModel? entry) => (entry ?? SelectedEntry) is { IsDirectory: false };

    /// <summary>
    /// The folder already showing <paramref name="fileName"/>'s content: one
    /// explored earlier, or the mount's own lazy `&lt;name&gt;.d/` view.
    /// </summary>
    private static FolderViewModel? ContentsOf(FolderViewModel folder, string fileName)
    {
        return folder.ContainerFor(fileName)
            ?? folder.Children.FirstOrDefault(c => c.Kind == FolderKind.Folder && c.Name == fileName + ".d");
    }

    private static bool HasContents(FolderViewModel folder, string fileName) => ContentsOf(folder, fileName) != null;

    /// <summary>Opens a file's content as a browsable folder (nested image, archive, filesystem).</summary>
    public async Task<bool> ExploreEntryAsync(EntryViewModel? entry)
    {
        if (entry == null || entry.IsDirectory) return false;
        var parent = await FolderOfAsync(entry);
        if (parent == null) return false;
        await parent.LoadAsync();
        var known = ContentsOf(parent, entry.Name);
        if (known != null)
        {
            entry.IsContainer = true;
            await GoToAsync(known);
            return true;
        }
        NestedTree? tree;
        using (BeginBusy($"Analyzing {entry.Name}…"))
        {
            try
            {
                var finder = _services.Finder;
                var source = parent.Source;
                tree = await Task.Run(() =>
                {
                    using var lease = source.Lease();
                    return lease == null ? null : NestedMount.TryMount(entry.Node, finder);
                });
            }
            catch (Exception ex)
            {
                Status = $"Could not open {entry.Name}: {ex.Message}";
                return false;
            }
        }
        if (tree == null)
        {
            Status = $"{entry.Name}: no browsable format found inside.";
            return false;
        }
        parent.Source.Own(tree.Owner);
        var container = parent.AddContainer(tree.Root, entry.Name, tree.Label);
        entry.IsContainer = true;
        await GoToAsync(container);
        Status = $"Opened {entry.Name} as {tree.Label}.";
        return true;
    }

    /// <summary>Shows a search hit in its own folder.</summary>
    [RelayCommand]
    private async Task ShowInFolderAsync(EntryViewModel? entry)
    {
        entry ??= SelectedEntry;
        if (entry == null) return;
        var folder = await FolderOfAsync(entry);
        if (folder == null) return;
        await GoToAsync(folder);
        SelectedEntry = Entries.FirstOrDefault(e => ReferenceEquals(e.Node, entry.Node));
    }

    /// <summary>Folder view model holding <paramref name="entry"/>, loading the path for search hits.</summary>
    private static async Task<FolderViewModel?> FolderOfAsync(EntryViewModel entry)
    {
        var folder = entry.BaseFolder;
        if (entry.RelativeDir.Length == 0) return folder;
        foreach (var part in entry.RelativeDir.Split('/'))
        {
            await folder.LoadAsync();
            var next = folder.Children.FirstOrDefault(c => c.Kind == FolderKind.Folder && c.Name == part);
            if (next == null) return null;
            folder = next;
        }
        return folder;
    }

    [RelayCommand]
    private async Task CopyPathAsync(EntryViewModel? entry)
    {
        entry ??= SelectedEntry;
        if (entry == null || Dialogs == null) return;
        await Dialogs.CopyTextAsync(entry.FullPath);
        Status = $"Copied {entry.FullPath}";
    }

    // ---- export ------------------------------------------------------------

    /// <summary>Exports the given entries (or the selection) into a folder the user picks.</summary>
    [RelayCommand]
    private async Task ExportAsync(IList? items)
    {
        var entries = items?.OfType<EntryViewModel>().ToList() ?? new List<EntryViewModel>();
        if (entries.Count == 0 && SelectedEntry != null) entries.Add(SelectedEntry);
        if (entries.Count == 0 || Dialogs == null) return;
        var dest = await Dialogs.PickFolderAsync(entries.Count == 1 ? $"Export “{entries[0].Name}” to…" : $"Export {entries.Count} items to…");
        if (dest != null)
        {
            await ExportToAsync(entries.Select(e => (e.Node, e.Name)).ToList(), dest, entries[0].BaseFolder.Source);
        }
    }

    [RelayCommand]
    private async Task ExportFolderAsync(FolderViewModel? folder)
    {
        folder ??= CurrentFolder;
        if (folder == null || Dialogs == null) return;
        var dest = await Dialogs.PickFolderAsync($"Export “{folder.Name}” to…");
        if (dest != null)
        {
            await ExportToAsync(new List<(IVfsNode, string)> { (folder.Node, folder.Name) }, dest, folder.Source);
        }
    }

    /// <summary>
    /// Writes each item under <paramref name="destDir"/> (folders recursively,
    /// same layout as `--export`). Returns the number of files written.
    /// </summary>
    public async Task<int> ExportToAsync(IReadOnlyList<(IVfsNode Node, string Name)> items, string destDir, SourceViewModel source)
    {
        var cts = new CancellationTokenSource();
        _operationCts = cts;
        int files = 0;
        string current = string.Empty;
        var work = Task.Run(() =>
        {
            using var lease = source.Lease() ?? throw new OperationCanceledException();
            var baseDir = Path.GetFullPath(destDir);
            Directory.CreateDirectory(baseDir);
            foreach (var (node, name) in items)
            {
                cts.Token.ThrowIfCancellationRequested();
                var target = VfsExport.SafeTarget(baseDir, name);
                if (target == null) continue;
                if (node.Type == VfsNodeType.Directory)
                {
                    VfsExport.Export(node, target, e =>
                    {
                        Volatile.Write(ref current, e.Path);
                        if (!e.IsDirectory) Interlocked.Increment(ref files);
                    }, cts.Token);
                }
                else
                {
                    Volatile.Write(ref current, name);
                    VfsExport.ExportFile(node, target);
                    Interlocked.Increment(ref files);
                }
            }
        }, cts.Token);
        using (BeginBusy("Exporting…"))
        {
            while (!work.IsCompleted)
            {
                BusyText = $"Exporting {Volatile.Read(ref files)} files… {Volatile.Read(ref current)}";
                await Task.WhenAny(work, Task.Delay(150));
            }
        }
        _operationCts = null;
        int written = Volatile.Read(ref files);
        if (work.IsCanceled || cts.IsCancellationRequested)
        {
            Status = $"Export cancelled after {written} files.";
        }
        else if (work.IsFaulted)
        {
            Status = $"Export failed after {written} files: {work.Exception?.GetBaseException().Message}";
        }
        else
        {
            Status = $"Exported {written} file{(written == 1 ? "" : "s")} to {destDir}.";
        }
        return written;
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        _operationCts?.Cancel();
        CancelSearch();
    }

    // ---- busy state ------------------------------------------------------

    private int _busyDepth;

    private IDisposable BeginBusy(string text)
    {
        _busyDepth++;
        BusyText = text;
        IsBusy = true;
        return new BusyScope(this);
    }

    private sealed class BusyScope(MainWindowViewModel owner) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            if (--owner._busyDepth == 0)
            {
                owner.IsBusy = false;
                owner.BusyText = null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelSearch();
        _operationCts?.Cancel();
        Preview.Dispose();
        foreach (var s in Sources) s.Dispose();
        Sources.Clear();
        Roots.Clear();
        _services.Dispose();
    }
}
