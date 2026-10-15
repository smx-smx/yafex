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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;

using Smx.SharpIO.Memory.Buffers;

using Yafex.Fuse;

namespace Yafex.Gui.ViewModels;

public sealed record InfoRow(string Key, string Value);

public enum PreviewTab
{
    Info = 0,
    Hex = 1,
    Text = 2,
    Image = 3,
}

/// <summary>
/// Right-hand pane: details, the whole file as virtualized hex, a text view of
/// the head, an image when the content is one, and what format it contains.
/// Reads happen off the UI thread; selecting another entry cancels them.
/// </summary>
public sealed partial class PreviewViewModel : ViewModelBase, IDisposable
{
    /// <summary>Bytes decoded for the text view.</summary>
    public const int TextLimit = 256 * 1024;

    /// <summary>Whole-file probe limit; larger files probe their head only.</summary>
    private const int ProbeLimit = 16 << 20;

    private const long ImageLimit = 64L << 20;

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasSelection { get; private set; }

    [ObservableProperty]
    public partial bool IsFile { get; private set; }

    [ObservableProperty]
    public partial HexDocument? Hex { get; private set; }

    [ObservableProperty]
    public partial string Text { get; private set; } = string.Empty;

    /// <summary>Shown above the text view (truncation, binary content).</summary>
    [ObservableProperty]
    public partial string? TextNote { get; private set; }

    [ObservableProperty]
    public partial Bitmap? Image { get; private set; }

    [ObservableProperty]
    public partial bool HasImage { get; private set; }

    /// <summary>Format found inside the file, or null (still probing / nothing).</summary>
    [ObservableProperty]
    public partial string? Detected { get; private set; }

    [ObservableProperty]
    public partial bool IsAnalyzing { get; private set; }

    [ObservableProperty]
    public partial int SelectedTab { get; set; }

    public ObservableCollection<InfoRow> Info { get; } = new();

    private int _hexBytesPerLine = 16;
    private Func<HexDocument>? _hexFactory;

    /// <summary>
    /// Fits hex rows to the pane: 16 bytes when there is room, else 8 or 4,
    /// so offset, hex and ASCII columns always show.
    /// </summary>
    /// <param name="columns">Monospace characters that fit in the hex view.</param>
    public void FitHex(int columns)
    {
        int perLine = columns >= 82 ? 16 : columns >= 44 ? 8 : 4;
        if (perLine == _hexBytesPerLine) return;
        _hexBytesPerLine = perLine;
        if (_hexFactory != null)
        {
            Hex?.Dispose();
            Hex = _hexFactory();
        }
    }

    /// <summary>The user picked Info explicitly: keep it instead of auto-picking a content tab.</summary>
    private bool _stickyInfo;

    partial void OnSelectedTabChanged(int value)
    {
        if (!_switching)
        {
            _stickyInfo = value == (int)PreviewTab.Info;
        }
    }

    private bool _switching;

    private void SwitchTab(PreviewTab tab)
    {
        if (_stickyInfo && tab != PreviewTab.Info) return;
        _switching = true;
        SelectedTab = (int)tab;
        _switching = false;
    }

    /// <summary>Entry being previewed, or null.</summary>
    public EntryViewModel? Entry { get; private set; }

    /// <summary>The file has stored bytes (`.raw` view) distinct from its content.</summary>
    [ObservableProperty]
    public partial bool HasStored { get; private set; }

    /// <summary>Hex/Text show the stored bytes instead of the (decrypted) content.</summary>
    [ObservableProperty]
    public partial bool ShowStored { get; set; }

    private FormatFinder? _finder;
    private CancellationTokenSource? _contentCts;
    private bool _resetting;

    partial void OnShowStoredChanged(bool value)
    {
        var entry = Entry;
        if (_resetting || entry == null || entry.IsDirectory) return;
        var target = value && entry.StoredView != null ? entry.StoredView : entry.Node;
        _ = LoadContentAsync(entry, target, detect: false);
    }

    public async Task ShowAsync(EntryViewModel? entry, FormatFinder? finder)
    {
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        Clear();
        if (entry == null || entry.BaseFolder.Source.IsClosed) return;
        Entry = entry;
        _finder = finder;
        var source = entry.BaseFolder.Source;

        HasSelection = true;
        Title = entry.Name;
        var node = entry.Node;
        Info.Add(new InfoRow("Name", entry.Name));
        Info.Add(new InfoRow("Path", entry.FullPath));
        Info.Add(new InfoRow("Type", entry.Kind));
        if (entry.IsDirectory)
        {
            if (node.Tree.Count > 0 || node is not LazyVfsDirectory)
            {
                Info.Add(new InfoRow("Items", node.Tree.Count.ToString()));
            }
            Info.Add(new InfoRow("Mode", Format.Mode(node.Mode, true)));
            SwitchTab(PreviewTab.Info);
            return;
        }

        IsFile = true;
        HasStored = entry.StoredView != null;
        Info.Add(new InfoRow("Size", Format.SizeDetailed(entry.Size)));
        Info.Add(new InfoRow("Mode", Format.Mode(node.Mode, false)));
        if (entry.StoredView != null)
        {
            Info.Add(new InfoRow("Stored size", Format.SizeDetailed(entry.StoredView.Size)));
        }
        if (entry.MetadataView != null)
        {
            var meta = entry.MetadataView;
            try
            {
                var text = await Task.Run(() =>
                {
                    using var lease = source.Lease() ?? throw new OperationCanceledException();
                    using var reader = new System.IO.StreamReader(VfsWalk.OpenRead(meta));
                    return reader.ReadToEnd();
                }, cts.Token);
                if (cts.IsCancellationRequested) return;
                foreach (var row in ParseMetadata(text))
                {
                    // sizes the format also reports are already shown above
                    if (long.TryParse(row.Value, out var n)
                        && (n == entry.Size || n == entry.StoredView?.Size)
                        && (row.Key == "Size" || row.Key == "Raw")) continue;
                    Info.Add(row);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Info.Add(new InfoRow("Metadata", $"unreadable: {ex.Message}"));
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
        await LoadContentAsync(entry, node, detect: true);
    }

    /// <summary>`key: value` lines as info rows; other lines under "Metadata".</summary>
    public static IEnumerable<InfoRow> ParseMetadata(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            int colon = line.IndexOf(':');
            if (colon > 0 && colon < 32)
            {
                var key = line[..colon].Trim();
                yield return new InfoRow(char.ToUpperInvariant(key[0]) + key[1..], line[(colon + 1)..].Trim());
            }
            else
            {
                yield return new InfoRow("Metadata", line);
            }
        }
    }

    /// <summary>
    /// Hex, text and image views of <paramref name="node"/> (the content or its
    /// stored bytes); <paramref name="detect"/> also sniffs for nested formats.
    /// </summary>
    private async Task LoadContentAsync(EntryViewModel entry, IVfsNode node, bool detect)
    {
        _contentCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);
        _contentCts = cts;
        var source = entry.BaseFolder.Source;
        var finder = _finder;

        Hex?.Dispose();
        long size = node.Size;
        _hexFactory = () => new HexDocument(() => VfsWalk.OpenRead(node), size, _hexBytesPerLine);
        Hex = _hexFactory();
        Text = string.Empty;
        TextNote = null;
        Image?.Dispose();
        Image = null;
        HasImage = false;
        if (detect) SwitchTab(PreviewTab.Hex);

        IsAnalyzing = true;
        try
        {
            var result = await Task.Run(() =>
            {
                using var lease = source.Lease() ?? throw new OperationCanceledException();
                return Analyze(node, detect ? finder : null, detect, cts.Token);
            }, cts.Token);
            if (cts.IsCancellationRequested) return;
            Text = result.Text;
            TextNote = result.TextNote;
            if (result.Image != null)
            {
                Image = result.Image;
                HasImage = true;
            }
            if (detect)
            {
                Detected = result.Detected;
                if (result.Detected != null)
                {
                    entry.IsContainer = true;
                    Info.Add(new InfoRow("Contains", result.Detected + " — open to browse"));
                }
                if (result.Error != null)
                {
                    Info.Add(new InfoRow("Read error", result.Error));
                }
                SwitchTab(result.Image != null ? PreviewTab.Image : result.LooksLikeText ? PreviewTab.Text : PreviewTab.Hex);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!cts.IsCancellationRequested) IsAnalyzing = false;
        }
    }

    private sealed record Analysis(string Text, string? TextNote, bool LooksLikeText, Bitmap? Image, string? Detected, string? Error);

    private static Analysis Analyze(IVfsNode node, FormatFinder? finder, bool detect, CancellationToken cancel)
    {
        var file = new VfsNodeVirtualFile(node);
        byte[] head;
        try
        {
            head = file.ReadHead(Math.Max(TextLimit, Math.Min(file.Length, ProbeLimit)));
        }
        catch (Exception ex)
        {
            return new Analysis(string.Empty, "Content could not be read.", false, null, null, ex.Message);
        }
        cancel.ThrowIfCancellationRequested();

        var textBytes = head.AsSpan(0, Math.Min(head.Length, TextLimit));
        bool looksLikeText = LooksLikeText(textBytes);
        string text = Decode(textBytes);
        string? note = null;
        if (!looksLikeText)
        {
            note = "Binary content: non-printable bytes are shown as '·'.";
        }
        if (file.Length > TextLimit)
        {
            note = (note != null ? note + " " : string.Empty) + $"Showing the first {Format.Size(TextLimit)} of {Format.Size(file.Length)}.";
        }

        Bitmap? image = null;
        if (IsImage(head) && file.Length <= ImageLimit)
        {
            try
            {
                using var s = file.OpenRead();
                image = new Bitmap(s);
            }
            catch (Exception)
            {
                // undecodable or no rendering platform (tests): no image tab
            }
        }
        cancel.ThrowIfCancellationRequested();

        string? detected = null;
        if (detect)
        {
            try
            {
                var expander = ContentExpanders.FindMatch(file);
                if (expander != null)
                {
                    detected = expander.Name;
                }
                else if (finder != null && head.Length > 0)
                {
                    var probe = new MemoryDataSource(new Memory64<byte>(head)) { Name = node.Name, Flags = DataSourceFlags.Input };
                    var hits = finder.ProbeAll(probe);
                    if (hits.Count > 0)
                    {
                        detected = Services.NestedMount.FormatName(hits[0].Format);
                    }
                }
            }
            catch (Exception)
            {
                // detection is a hint only
            }
        }
        return new Analysis(text, note, looksLikeText, image, detected, null);
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) return true;
        int sample = Math.Min(bytes.Length, 8192);
        int bad = 0;
        for (int i = 0; i < sample; i++)
        {
            byte b = bytes[i];
            if (b == 0) return false;
            if (b < 0x20 && b is not (byte)'\n' and not (byte)'\r' and not (byte)'\t') bad++;
        }
        return bad * 20 < sample;
    }

    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        string text = Encoding.UTF8.GetString(bytes);
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(c is '\n' or '\t' || (c >= ' ' && c != '�' && c != '\u007F') ? c : c == '\r' ? '\r' : '·');
        }
        return sb.ToString();
    }

    private static bool IsImage(byte[] head)
    {
        ReadOnlySpan<byte> h = head;
        return h.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47])    // PNG
            || h.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])          // JPEG
            || h.StartsWith("GIF8"u8)
            || h.StartsWith("BM"u8) && h.Length > 14
            || h.Length > 12 && h.StartsWith("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8);
    }

    /// <summary>Drops the preview (e.g. its image is being closed).</summary>
    public void Reset()
    {
        _cts?.Cancel();
        Clear();
    }

    private void Clear()
    {
        _contentCts?.Cancel();
        _resetting = true;
        ShowStored = false;
        _resetting = false;
        HasStored = false;
        Entry = null;
        HasSelection = false;
        IsFile = false;
        IsAnalyzing = false;
        Title = string.Empty;
        Info.Clear();
        _hexFactory = null;
        Hex?.Dispose();
        Hex = null;
        Text = string.Empty;
        TextNote = null;
        Image?.Dispose();
        Image = null;
        HasImage = false;
        Detected = null;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        Clear();
    }
}
