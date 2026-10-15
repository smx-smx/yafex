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
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

using Yafex;
using Yafex.Fuse;
using Yafex.Gui.Services;
using Yafex.Support;

namespace Yafex.Gui.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
    private const long PreviewLimit = 262144;

    private readonly GuiServices _services = new();
    private MFile? _input;
    private bool _disposed;
    private string _status = "Open a firmware image to begin.";
    private string _probeLine = string.Empty;
    private string _hexPreview = string.Empty;
    private string _textPreview = string.Empty;
    private string _infoPreview = string.Empty;

    public MainViewModel()
    {
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string ProbeLine
    {
        get => _probeLine;
        set => SetProperty(ref _probeLine, value);
    }

    public string HexPreview
    {
        get => _hexPreview;
        set => SetProperty(ref _hexPreview, value);
    }

    public string TextPreview
    {
        get => _textPreview;
        set => SetProperty(ref _textPreview, value);
    }

    public string InfoPreview
    {
        get => _infoPreview;
        set => SetProperty(ref _infoPreview, value);
    }

    public ObservableCollection<FsNodeViewModel> Roots { get; } = new();

    private FsNodeViewModel? _selected;
    public FsNodeViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                ShowPreview(value);
            }
        }
    }

    public void SetKeysFile(string? path)
    {
        var error = _services.Configure(path);
        Status = error != null ? $"Keys not loaded: {error}"
            : path == null ? "Keys cleared." : $"Keys loaded from {path}.";
    }

    public void OpenFile(string path)
    {
        CloseFile();
        try
        {
            var input = new MFile(path);
            var hits = _services.Finder.ProbeAll(input);
            if (hits.Count == 0)
            {
                input.Dispose();
                Status = $"No format identified for {System.IO.Path.GetFileName(path)}.";
                return;
            }
            ProbeLine = string.Join("  ", hits.Select(h =>
                $"{Enum.GetName(h.Format)?.ToLowerInvariant()}:{h.Result.Confidence}"));
            var (addon, result) = _services.Finder.DetectFormatAddon(input);
            if (addon == null || result == null)
            {
                input.Dispose();
                Status = "Detection failed.";
                return;
            }
            var vfs = addon.CreateVfs(input, result);
            if (vfs == null)
            {
                input.Dispose();
                Status = $"Lazy VFS not supported for {Enum.GetName(addon.FileFormat)?.ToLowerInvariant()}.";
                return;
            }
            _input = input;
            Roots.Clear();
            foreach (var link in vfs.Root.Tree.Values.OrderBy(l => l.Node.Name))
            {
                Roots.Add(new FsNodeViewModel(link.Node));
            }
            Status = $"{System.IO.Path.GetFileName(path)} — {Enum.GetName(addon.FileFormat)?.ToLowerInvariant()}";
        }
        catch (Exception ex)
        {
            CloseFile();
            Status = $"Open failed: {ex.Message}";
        }
    }

    public void CloseFile()
    {
        Roots.Clear();
        ProbeLine = string.Empty;
        HexPreview = TextPreview = InfoPreview = string.Empty;
        _input?.Dispose();
        _input = null;
        GC.Collect();
    }

    private void ShowPreview(FsNodeViewModel? node)
    {
        HexPreview = TextPreview = InfoPreview = string.Empty;
        if (node == null) return;
        var inner = node.Node;
        InfoPreview = $"name: {inner.Name}\ntype: {inner.Type}\nsize: {inner.Size}\nmode: {Convert.ToString(inner.Mode, 8)}";
        if (inner is not IVfsFile file || inner.Size <= 0) return;
        try
        {
            long count = Math.Min(inner.Size, PreviewLimit);
            var buf = file.Read(0, count);
            if (buf == null || buf.Length == 0) return;
            HexPreview = RenderHex(buf);
            TextPreview = RenderText(buf);
            if (inner.Size > PreviewLimit)
            {
                InfoPreview += $"\npreview: first {buf.Length} of {inner.Size} bytes";
            }
        }
        catch (Exception ex)
        {
            InfoPreview += $"\nread failed: {ex.Message}";
        }
    }

    private static string RenderHex(byte[] buf)
    {
        var sb = new StringBuilder(buf.Length / 16 * 80);
        for (int i = 0; i < buf.Length; i += 16)
        {
            sb.Append($"{i:X8}  ");
            for (int j = 0; j < 16; j++)
            {
                sb.Append(j < buf.Length - i ? $"{buf[i + j]:X2} " : "   ");
                if (j == 7) sb.Append(' ');
            }
            sb.Append(" |");
            for (int j = 0; j < 16 && i + j < buf.Length; j++)
            {
                char c = (char)buf[i + j];
                sb.Append(c is >= ' ' and <= '~' ? c : '.');
            }
            sb.AppendLine("|");
        }
        return sb.ToString();
    }

    private static string RenderText(byte[] buf)
    {
        string text = Encoding.UTF8.GetString(buf);
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(c is '\r' or '\n' or '\t' || (c >= ' ' && c != '\uFFFD') ? c : '.');
        }
        return sb.ToString();
    }

    /// <summary>Writes the selected file node to disk. Returns null when nothing to save.</summary>
    public string? SaveSelected(string path)
    {
        if (Selected?.Node is not IVfsFile file || Selected.Node.Size <= 0) return null;
        try
        {
            using var out_ = System.IO.File.Open(path, System.IO.FileMode.Create, System.IO.FileAccess.Write);
            long done = 0;
            while (done < Selected.Node.Size)
            {
                var chunk = file.Read(done, Math.Min(1 << 20, Selected.Node.Size - done));
                if (chunk == null || chunk.Length == 0) break;
                out_.Write(chunk, 0, chunk.Length);
                done += chunk.Length;
            }
            return path;
        }
        catch (Exception ex)
        {
            Status = $"Save failed: {ex.Message}";
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseFile();
        _services.Dispose();
    }
}
