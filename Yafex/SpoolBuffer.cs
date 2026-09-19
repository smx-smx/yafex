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
using System.IO;

using Smx.SharpIO.Memory.Buffers;

using Yafex.Metadata;
using Yafex.Support;

namespace Yafex;

/// <summary>Per-artifact storage choice. <see cref="Auto"/> streams straight to the
/// final file past the threshold instead of accumulating RAM.</summary>
public enum SpoolHint
{
    Auto,
    Memory,
    File
}

/// <summary>
/// Process-wide spool policy. Big artifacts (e.g. rootfs images) stream directly
/// to their final destination instead of sitting fully in RAM. There is no temp
/// layer: below the threshold artifacts stay in memory, above it they never do.
/// </summary>
public static class ArtifactSpool
{
    public const long DefaultThresholdBytes = 64L << 20;

    private static long _thresholdBytes = DefaultThresholdBytes;
    public static long ThresholdBytes
    {
        get => _thresholdBytes;
        set => _thresholdBytes = value;
    }
}

/// <summary>Common surface of memory- and file-backed artifact buffers.</summary>
public interface IArtifactBuffer : IDisposable
{
    void AddMetadata(IArtifactMetadata metadata);
    void Write(byte[] data);
    void Write(ReadOnlySpan64<byte> data);
    void WriteAt(int index, ReadOnlySpan64<byte> data);
    IDataSource ToDataSource();
}

/// <summary>
/// IDataSource over an already-written final file, exposed as memory-mapped
/// <see cref="Memory64{T}"/> so detectors and nested extraction work unchanged.
/// </summary>
public sealed class FileDataSource : BaseDataSource, IDataSource, IDisposable
{
    private readonly MFile _owner;
    private bool _disposed;

    public FileDataSource(string path, MFile owner)
    {
        Path = path;
        _owner = owner;
    }

    /// <summary>Final destination path this artifact was streamed to.</summary>
    public string Path { get; }

    public Memory64<byte> Data => _owner.Data;

    public IEnumerable<byte> Bytes => Data.ToEnumerable();

    public DataSourceFlags Flags { get; set; }

    public string Name { get; set; } = "";

    public string? Directory { get; set; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.Dispose();
    }
}

/// <summary>
/// Buffer that accumulates in memory and, past <see cref="ArtifactSpool.ThresholdBytes"/>
/// (or immediately with <see cref="SpoolHint.File"/>), streams to a
/// `<paramref name="finalPath"/>.partial-*` sidecar in the SAME directory and atomically
/// renames it to the final name at <see cref="ToDataSource"/> time. Same-dir rename is
/// atomic on NTFS and POSIX, so a crash can only leave clearly-marked `.partial` files,
/// never corrupt-looking outputs. Materializes to <see cref="MemoryDataSource"/> or
/// <see cref="FileDataSource"/>; both expose identical metadata + Data.
/// </summary>
public sealed class SpoolBuffer : BaseDataSource, IArtifactBuffer
{
    private readonly string _finalPath;
    private readonly string _name;
    private readonly DataSourceFlags _flags;
    private readonly SpoolHint _hint;

    private MemoryStream? _mem;
    private FileStream? _file;
    private string? _spoolPath;
    private bool _disposed;

    public SpoolBuffer(string finalPath, string name, DataSourceFlags flags, SpoolHint hint = SpoolHint.Auto)
    {
        _finalPath = finalPath;
        _name = name;
        _flags = flags;
        _hint = hint;
        if (hint == SpoolHint.File)
        {
            OpenFile();
        }
        else
        {
            _mem = new MemoryStream();
        }
    }

    public void Write(byte[] data)
    {
        WriteSpan(data);
    }

    public void Write(ReadOnlySpan64<byte> data)
    {
        foreach (var chunk in data.GetChunks())
        {
            WriteSpan(chunk);
        }
    }

    private void WriteSpan(ReadOnlySpan<byte> span)
    {
        EnsureCapacity(span.Length);
        if (_file != null) _file.Write(span);
        else _mem!.Write(span);
    }

    public void WriteAt(int index, ReadOnlySpan64<byte> data)
    {
        if (_file != null)
        {
            var saved = _file.Position;
            _file.Position = index;
            foreach (var chunk in data.GetChunks())
            {
                ReadOnlySpan<byte> span = chunk;
                _file.Write(span);
            }
            _file.Position = saved;
            return;
        }
        if (_mem!.Length < index + data.Length && _mem.Length + data.Length >= ArtifactSpool.ThresholdBytes)
        {
            OpenFile();
            WriteAt(index, data);
            return;
        }
        var savedPos = _mem.Position;
        _mem.Position = index;
        foreach (var chunk in data.GetChunks())
        {
            ReadOnlySpan<byte> span = chunk;
            _mem.Write(span);
        }
        _mem.Position = savedPos;
    }

    private long Length => _file != null ? _file.Length : _mem!.Length;

    private void EnsureCapacity(long incoming)
    {
        if (_hint != SpoolHint.Auto || _file != null) return;
        if (Length + incoming >= ArtifactSpool.ThresholdBytes)
        {
            OpenFile();
        }
    }

    private void OpenFile()
    {
        _spoolPath = $"{_finalPath}.partial-{Guid.NewGuid():N}";
        var dir = Path.GetDirectoryName(_spoolPath);
        if (dir != null) Directory.CreateDirectory(dir);
        _file = new FileStream(_spoolPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        if (_mem != null)
        {
            _mem.Position = 0;
            _mem.CopyTo(_file);
            _mem.Dispose();
            _mem = null;
        }
    }

    public IDataSource ToDataSource()
    {
        if (_file != null)
        {
            _file.Flush();
            var length = _file.Length;
            _file.Dispose();
            _file = null;
            if (length == 0)
            {
                TryDelete(_spoolPath!);
                return FinishMemory(Array.Empty<byte>());
            }
            // same-dir rename: atomic on NTFS and POSIX
            var spoolPath = _spoolPath!;
            _spoolPath = null;
            if (File.Exists(_finalPath)) File.Delete(_finalPath);
            File.Move(spoolPath, _finalPath);
            var owner = new MFile(_finalPath, readOnly: true);
            var ds = new FileDataSource(_finalPath, owner)
            {
                Name = _name,
                Flags = _flags
            };
            CopyMetadataTo(ds);
            return ds;
        }
        return FinishMemory(_mem!.ToArray());
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private MemoryDataSource FinishMemory(byte[] bytes)
    {
        var ds = new MemoryDataSource(new Memory64<byte>(bytes))
        {
            Name = _name,
            Flags = _flags
        };
        CopyMetadataTo(ds);
        return ds;
    }

    private void CopyMetadataTo(BaseDataSource ds)
    {
        foreach (var bucket in Metadata.Values)
        {
            foreach (var md in bucket)
            {
                ds.AddMetadata(md);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mem?.Dispose();
        _mem = null;
        _file?.Dispose();
        _file = null;
        // never completed (e.g. extractor threw): don't litter .partial files
        if (_spoolPath != null)
        {
            TryDelete(_spoolPath);
            _spoolPath = null;
        }
    }
}
