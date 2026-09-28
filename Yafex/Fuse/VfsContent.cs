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
using System.IO.Compression;
using System.Linq;
using System.Text;

using DiscUtils.SquashFs;

namespace Yafex.Fuse;

/// <summary>
/// A directory whose children are built on first access (first readdir/lookup),
/// not at mount time. Browsing a huge tree costs nothing until you open it.
/// </summary>
public sealed class LazyVfsDirectory : VfsNode, IVfsDir
{
    private readonly Func<IEnumerable<IVfsNode>> _factory;
    private readonly object _gate = new();
    private bool _loaded;

    private static readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof(LazyVfsDirectory));

    public LazyVfsDirectory(string name, int mode, Func<IEnumerable<IVfsNode>> factory)
        : base(name, mode, 0)
    {
        _factory = factory;
    }

    public override VfsNodeType Type => VfsNodeType.Directory;

    public override void EnsureLoaded()
    {
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                foreach (var child in _factory())
                {
                    AddNode(child);
                }
            }
            catch (Exception ex)
            {
                // a single hostile subtree (e.g. squashfs symlinks DiscUtils
                // cannot parse) must not break the mount: empty dir, logged
                log.Error($"lazy expansion of '{Name}' failed", ex);
            }
        }
    }
}

/// <summary>Which views of an image a format opts in to when mounting.</summary>
[Flags]
public enum VfsImageViews
{
    None = 0,
    /// <summary>Decrypted content under the image name.</summary>
    Decrypted = 1 << 0,
    /// <summary>Stored (e.g. still encrypted) bytes as `<name>.raw`.</summary>
    Raw = 1 << 1,
    /// <summary>Text metadata as `<name>.meta`.</summary>
    Metadata = 1 << 2,
    /// <summary>Browsable content as `<name>.d/` (expander registry).</summary>
    Expanded = 1 << 3,
    All = Decrypted | Raw | Metadata | Expanded,
}

/// <summary>
/// Probes a virtual file and, on match, builds its browsable content.
/// Expensive work (reader construction, tree walks) belongs in the returned
/// factory so directories stay lazy. Register in <see cref="ContentExpanders"/>.
/// </summary>
public interface IContentExpander
{
    string Name { get; }
    bool Matches(IVirtualFile file);
    IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive);
}

/// <summary>Ordered expander registry shared by all formats. First match wins.</summary>
public static class ContentExpanders
{
    private static readonly List<IContentExpander> _expanders = new()
    {
        new Squashfs3Expander(),
        new SquashfsExpander(),
        new GzipExpander(),
        new FatExpander(),
        new LzhsFsExpander(),
        new LzhsExpander(),
        new GptExpander(),
        new VdfsExpander(),
        new LgLzoExpander(),
        new CramfsExpander(),
        new NfsbExpander(),
    };

    public static IReadOnlyList<IContentExpander> All => _expanders;

    public static void Register(IContentExpander expander) => _expanders.Add(expander);

    public static bool TryExpand(IVirtualFile file, ICollection<object> keepAlive, out IVfsNode? dir)
    {
        dir = null;
        foreach (var expander in _expanders)
        {
            try
            {
                if (!expander.Matches(file)) continue;
                dir = expander.Expand(file, keepAlive);
                return true;
            }
            catch (Exception)
            {
                // faulty match: try the next expander
            }
        }
        return false;
    }
}

public sealed class SquashfsExpander : IContentExpander
{
    public string Name => "squashfs";

    public bool Matches(IVirtualFile file)
    {
        try
        {
            using var probe = file.OpenRead();
            var magic = new byte[4];
            if (probe.Read(magic, 0, 4) != 4) return false;
            return BitConverter.ToUInt32(magic, 0) == VfsSquashfs.HsqsMagic;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            if (!VfsSquashfs.TryOpen(file, keepAlive, out var reader))
            {
                throw new InvalidDataException("squashfs open failed");
            }
            var tmp = new YafexDirectory(file.Name, Helpers.OctalLiteral(0755));
            VfsSquashfs.BuildTree(tmp, reader);
            return tmp.Tree.Values.Select(l => l.Node);
        });
    }
}

public sealed class GzipExpander : IContentExpander
{
    public string Name => "gzip";

    public bool Matches(IVirtualFile file)
    {
        try
        {
            using var probe = file.OpenRead();
            var magic = new byte[2];
            if (probe.Read(magic, 0, 2) != 2) return false;
            return magic[0] == 0x1F && magic[1] == 0x8B;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        var stem = file.Name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? file.Name[..^3]
            : file.Name + ".out";
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            var inflated = new GzipVirtualFile(stem, file);
            return new IVfsNode[] { new PakVfsFile(inflated, Helpers.OctalLiteral(0444)) };
        });
    }
}

public sealed class FatExpander : IContentExpander
{
    public string Name => "fat";

    public bool Matches(IVirtualFile file)
    {
        try
        {
            using var probe = file.OpenRead();
            if (probe.Length < 512) return false;
            var bpb = new byte[512];
            int off = 0;
            while (off < bpb.Length)
            {
                int n = probe.Read(bpb, off, bpb.Length - off);
                if (n <= 0) return false;
                off += n;
            }
            return VfsFat.IsBpb(bpb);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            if (!VfsFat.TryOpen(file, keepAlive, out var fs))
            {
                throw new InvalidDataException("fat open failed");
            }
            return VfsExt.BuildLazyTree(fs);
        });
    }
}

/// <summary>
/// Decompressed view of a gzip member. Length comes from the ISIZE trailer
/// without inflating; reads inflate forward from the start (backward seeks
/// restart the inflater). Single-member streams.
/// </summary>
public sealed class GzipVirtualFile : IVirtualFile
{
    private readonly IVirtualFile _source;

    public GzipVirtualFile(string name, IVirtualFile source)
    {
        Name = name;
        _source = source;
    }

    public string Name { get; }

    public long Length
    {
        get
        {
            using var s = _source.OpenRead();
            if (s.Length < 4) throw new InvalidDataException("truncated gzip");
            s.Seek(-4, SeekOrigin.End);
            var isize = new byte[4];
            Fill(s, isize);
            return BitConverter.ToUInt32(isize, 0);
        }
    }

    public Stream OpenRead() => new InflateStream(() => _source.OpenRead(), Length);

    private static void Fill(Stream s, byte[] buf)
    {
        var off = 0;
        while (off < buf.Length)
        {
            var n = s.Read(buf, off, buf.Length - off);
            if (n <= 0) throw new InvalidDataException("truncated gzip");
            off += n;
        }
    }
}

/// <summary>
/// Forward-inflating stream with restart-on-backward-seek. RAM stays O(window);
/// seeking backwards replays from the start (documented cost of on-the-fly).
/// </summary>
public sealed class InflateStream : Stream
{
    private readonly Func<Stream> _opener;
    private readonly long _length;
    private Stream _raw;
    private GZipStream _inflater;
    private long _position;

    public InflateStream(Func<Stream> opener, long length)
    {
        _opener = opener;
        _length = length;
        _raw = opener();
        _inflater = new GZipStream(_raw, CompressionMode.Decompress);
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _length) return 0;
        count = (int)Math.Min(count, _length - _position);
        var total = 0;
        while (total < count)
        {
            var n = _inflater.Read(buffer, offset + total, count - total);
            if (n <= 0) break;
            total += n;
        }
        _position += total;
        return total;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (target < 0) throw new IOException("seek before start");
        if (target < _position) Restart();
        Skip(target - _position);
        return _position;
    }

    private void Restart()
    {
        _inflater.Dispose();
        _raw.Dispose();
        _raw = _opener();
        _inflater = new GZipStream(_raw, CompressionMode.Decompress);
        _position = 0;
    }

    private void Skip(long n)
    {
        var buf = new byte[Math.Min(n, 1 << 20)];
        while (n > 0)
        {
            var take = (int)Math.Min(buf.Length, n);
            var r = Read(buf, 0, take);
            if (r <= 0) break;
            n -= r;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inflater.Dispose();
            _raw.Dispose();
        }
        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>In-memory text file (metadata sidecars).</summary>
public sealed class TextVirtualFile : IVirtualFile
{
    private readonly byte[] _content;

    public TextVirtualFile(string name, string content)
    {
        Name = name;
        _content = Encoding.UTF8.GetBytes(content);
    }

    public string Name { get; }
    public long Length => _content.Length;
    public Stream OpenRead() => new MemoryStream(_content, writable: false);
}

/// <summary>
/// Windowed read-only stream over an <see cref="IDataSource"/>'s mapped data:
/// seeks without materializing, reads only the requested window.
/// </summary>
public sealed class DataSourceRangeStream : Stream
{
    private readonly IDataSource _source;
    private readonly long _base;
    private readonly long _length;
    private long _position;

    public DataSourceRangeStream(IDataSource source, long offset, long length)
    {
        _source = source;
        _base = offset;
        _length = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _length) return 0;
        count = (int)Math.Min(count, _length - _position);
        var window = _source.Data.Slice(_base + _position, count).ToArray();
        window.CopyTo(buffer, offset);
        _position += window.Length;
        return window.Length;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (target < 0 || target > _length) throw new IOException("seek out of range");
        _position = target;
        return target;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>A stored-bytes window of a data source, exposed lazily (raw views).</summary>
public sealed class RangeVirtualFile : IVirtualFile
{
    private readonly IDataSource _source;
    private readonly long _offset;

    public RangeVirtualFile(string name, IDataSource source, long offset, long length)
    {
        Name = name;
        _source = source;
        _offset = offset;
        Length = length;
    }

    public string Name { get; }
    public long Length { get; }
    public Stream OpenRead() => new DataSourceRangeStream(_source, _offset, Length);
}

/// <summary>A byte window of another virtual file, exposed lazily (partition
/// slices, stored member regions). Backing streams open per read.</summary>
public sealed class SliceVirtualFile : IVirtualFile
{
    private readonly IVirtualFile _source;
    private readonly long _offset;

    public SliceVirtualFile(string name, IVirtualFile source, long offset, long length)
    {
        Name = name;
        _source = source;
        _offset = offset;
        Length = length;
    }

    public string Name { get; }
    public long Length { get; }
    public Stream OpenRead() => new SliceStream(_source, _offset, Length);
}

/// <summary>Windowed read-only stream over an <see cref="IVirtualFile"/>.</summary>
public sealed class SliceStream : Stream
{
    private readonly IVirtualFile _source;
    private readonly long _base;
    private readonly long _length;
    private long _position;
    private Stream? _current;
    private long _currentStart = -1;

    public SliceStream(IVirtualFile source, long offset, long length)
    {
        _source = source;
        _base = offset;
        _length = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _length) return 0;
        count = (int)Math.Min(count, _length - _position);
        var stream = StreamAt(_position);
        int n = stream.Read(buffer, offset, count);
        _position += n;
        _currentStart = _position;
        return n;
    }

    private Stream StreamAt(long pos)
    {
        // Sequential reads ride the open backing stream; any gap reopens
        // (cheap) and seeks (the backing file is randomly accessible).
        if (_current == null || pos < _currentStart)
        {
            _current?.Dispose();
            _current = _source.OpenRead();
            _current.Seek(_base + pos, SeekOrigin.Begin);
            _currentStart = pos;
        }
        else if (pos > _currentStart)
        {
            _current.Seek(_base + pos, SeekOrigin.Begin);
            _currentStart = pos;
        }
        return _current;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (target < 0 || target > _length) throw new IOException("seek out of range");
        _position = target;
        return target;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _current?.Dispose();
        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// Mounts one image with the opted-in views: decrypted file, `.raw` stored bytes,
/// `.meta` text, and `.d/` expanded directory. Formats opt in via
/// <see cref="VfsImageViews"/>; raw/describe suppliers are optional per format.
/// </summary>
public static class VfsBuilder
{
    public static void AddImage(
        IVfsNode parent,
        IVirtualFile image,
        VfsImageViews views,
        ICollection<object> keepAlive,
        Func<IVirtualFile, VfsNode>? makeFile = null,
        IVirtualFile? raw = null,
        Func<string>? describe = null)
    {
        makeFile ??= img => new PakVfsFile(img, Helpers.OctalLiteral(0444));

        if (views.HasFlag(VfsImageViews.Decrypted))
        {
            parent.AddNode(makeFile(image));
        }
        if (views.HasFlag(VfsImageViews.Raw) && raw != null)
        {
            parent.AddNode(makeFile(new RenamedVirtualFile(raw, image.Name + ".raw")));
        }
        if (views.HasFlag(VfsImageViews.Metadata) && describe != null)
        {
            parent.AddNode(makeFile(new TextVirtualFile(image.Name + ".meta", describe())));
        }
        if (views.HasFlag(VfsImageViews.Expanded))
        {
            // cheap magic probe at mount; the directory itself stays lazy
            foreach (var expander in ContentExpanders.All)
            {
                var exp = expander;
                bool match;
                try { match = exp.Matches(image); }
                catch (Exception) { continue; }
                if (!match) continue;
                parent.AddNode(new LazyVfsDirectory(image.Name + ".d", Helpers.OctalLiteral(0755), () =>
                {
                    var dir = exp.Expand(image, keepAlive);
                    dir.EnsureLoaded();
                    return dir.Tree.Values.Select(l => l.Node).ToList();
                }));
                break;
            }
        }
    }

    private sealed class RenamedVirtualFile : IVirtualFile
    {
        private readonly IVirtualFile _inner;
        public RenamedVirtualFile(IVirtualFile inner, string name) { _inner = inner; Name = name; }
        public string Name { get; }
        public long Length => _inner.Length;
        public Stream OpenRead() => _inner.OpenRead();
    }
}
