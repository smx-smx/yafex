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
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Yafex.Gui.ViewModels;

/// <summary>One row of the hex view.</summary>
public sealed record HexLine(int Index, string Offset, string Hex, string Ascii);

/// <summary>
/// The whole file as hex rows, produced on demand: the list virtualizes, so
/// only visible rows are formatted and only their 64 KiB pages are read
/// (small LRU cache). Implements non-generic <see cref="IList"/> so item
/// controls index it instead of enumerating the file.
/// </summary>
public sealed class HexDocument : IList, IReadOnlyList<HexLine>, IDisposable
{
    private const int PageSize = 64 * 1024;
    private const int MaxPages = 32;

    private readonly Func<Stream> _open;
    private readonly long _length;
    private readonly int _offsetDigits;
    private readonly Dictionary<long, byte[]> _pages = new();
    private readonly LinkedList<long> _lru = new();
    private readonly object _gate = new();
    private Stream? _stream;
    private bool _disposed;

    public HexDocument(Func<Stream> open, long length, int bytesPerLine = 16)
    {
        _open = open;
        _length = length;
        BytesPerLine = bytesPerLine;
        _offsetDigits = length > 0xFFFFFFFFL ? 12 : 8;
    }

    /// <summary>Bytes per row (4, 8 or 16; rows of 16 get a gap after 8).</summary>
    public int BytesPerLine { get; }

    public long Length => _length;

    public int Count => (int)Math.Min(int.MaxValue, (_length + BytesPerLine - 1) / BytesPerLine);

    public HexLine this[int index] => Line(index);

    object? IList.this[int index]
    {
        get => Line(index);
        set => throw new NotSupportedException();
    }

    private HexLine Line(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        long offset = (long)index * BytesPerLine;
        int n = (int)Math.Min(BytesPerLine, _length - offset);
        var bytes = new byte[n];
        int got;
        try
        {
            got = Read(offset, bytes);
        }
        catch (Exception)
        {
            got = -1;
        }
        var hex = new StringBuilder(BytesPerLine * 3 + 1);
        var ascii = new StringBuilder(BytesPerLine);
        for (int i = 0; i < BytesPerLine; i++)
        {
            if (i == 8 && BytesPerLine == 16) hex.Append(' ');
            if (i >= n)
            {
                hex.Append("   ");
            }
            else if (i >= got)
            {
                hex.Append("?? ");
                ascii.Append('?');
            }
            else
            {
                byte b = bytes[i];
                hex.Append(b.ToString("X2")).Append(' ');
                ascii.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }
        }
        return new HexLine(index, offset.ToString("X" + _offsetDigits), hex.ToString(), ascii.ToString());
    }

    /// <summary>Bytes at <paramref name="offset"/> from the page cache; returns how many were read.</summary>
    private int Read(long offset, byte[] dest)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int done = 0;
            while (done < dest.Length)
            {
                long pos = offset + done;
                var page = Page(pos / PageSize);
                int inPage = (int)(pos % PageSize);
                if (inPage >= page.Length) break;
                int take = Math.Min(dest.Length - done, page.Length - inPage);
                Array.Copy(page, inPage, dest, done, take);
                done += take;
            }
            return done;
        }
    }

    private byte[] Page(long number)
    {
        if (_pages.TryGetValue(number, out var cached))
        {
            _lru.Remove(number);
            _lru.AddFirst(number);
            return cached;
        }
        _stream ??= _open();
        long start = number * PageSize;
        var page = new byte[(int)Math.Min(PageSize, _length - start)];
        _stream.Seek(start, SeekOrigin.Begin);
        int got = _stream.ReadAtLeast(page, page.Length, throwOnEndOfStream: false);
        if (got < page.Length) page = page[..got];
        _pages[number] = page;
        _lru.AddFirst(number);
        if (_lru.Count > MaxPages)
        {
            _pages.Remove(_lru.Last!.Value);
            _lru.RemoveLast();
        }
        return page;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _stream?.Dispose();
            _pages.Clear();
        }
    }

    public IEnumerator<HexLine> GetEnumerator()
    {
        for (int i = 0; i < Count; i++) yield return Line(i);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool IsFixedSize => true;
    public bool IsReadOnly => true;
    public bool IsSynchronized => false;
    public object SyncRoot => _gate;
    public bool Contains(object? value) => value is HexLine l && l.Index < Count;
    public int IndexOf(object? value) => value is HexLine l && l.Index < Count ? l.Index : -1;
    public void CopyTo(Array array, int index)
    {
        for (int i = 0; i < Count; i++) array.SetValue(Line(i), index + i);
    }
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}
