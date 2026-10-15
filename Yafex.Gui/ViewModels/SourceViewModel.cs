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

using Yafex.Gui.Services;

namespace Yafex.Gui.ViewModels;

/// <summary>
/// An opened image: its tree root plus everything mapped/buffered for it.
/// Background readers hold a <see cref="Lease"/>; closing waits for the last
/// one before unmapping, so in-flight reads never touch released memory.
/// </summary>
public sealed class SourceViewModel : IDisposable
{
    private readonly List<IDisposable> _owned = new();
    private readonly object _gate = new();
    private int _leases;
    private bool _closing;
    private bool _disposed;

    public SourceViewModel(string path, NestedTree tree, IReadOnlyList<string> matches)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        Format = tree.Label;
        Matches = matches;
        _owned.Add(tree.Owner);
        Root = new FolderViewModel(tree.Root, FileName, null, this, FolderKind.Source, tree.Label);
    }

    public string Path { get; }

    public string FileName { get; }

    /// <summary>Format the tree was built from.</summary>
    public string Format { get; }

    /// <summary>Every format that matched the file, best first, with confidence.</summary>
    public IReadOnlyList<string> Matches { get; }

    public FolderViewModel Root { get; }

    public bool IsClosed
    {
        get { lock (_gate) return _closing; }
    }

    /// <summary>Ties a nested tree's buffers to this source's lifetime.</summary>
    public void Own(IDisposable owner)
    {
        lock (_gate)
        {
            if (!_closing)
            {
                _owned.Add(owner);
                return;
            }
        }
        owner.Dispose();
    }

    /// <summary>
    /// Keeps the source's data mapped until the lease is disposed; null when
    /// the source is already closing (the caller must not read it).
    /// </summary>
    public IDisposable? Lease()
    {
        lock (_gate)
        {
            if (_closing) return null;
            _leases++;
            return new LeaseHandle(this);
        }
    }

    private void Release()
    {
        bool release;
        lock (_gate)
        {
            _leases--;
            release = _closing && _leases == 0;
        }
        if (release) ReleaseOwned();
    }

    /// <summary>Closes the source; data is released once no lease remains.</summary>
    public void Dispose()
    {
        bool release;
        lock (_gate)
        {
            if (_closing) return;
            _closing = true;
            release = _leases == 0;
        }
        if (release) ReleaseOwned();
    }

    private void ReleaseOwned()
    {
        List<IDisposable> owned;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            owned = new List<IDisposable>(_owned);
            _owned.Clear();
        }
        foreach (var o in owned)
        {
            try { o.Dispose(); } catch (Exception) { }
        }
    }

    private sealed class LeaseHandle(SourceViewModel owner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.Release();
            }
        }
    }
}
