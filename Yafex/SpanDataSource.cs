using System;

using Smx.SharpIO.Memory.Buffers;

namespace Yafex;
public class SpanDataSource : BaseDataSource, IDataSource
{
    private string? _name;
    private string? _path;
    private int _offset;
    private int _length;

    public SpanDataSource(IDataSource baseFile, int offset, int length)
    {
        _name = baseFile.Name;
        _path = baseFile.Directory;
    }

    public string? Name
    {
        get => _name;
        set => _name = value;
    }

    public string? Directory {
        get => _path;
        set => _path = value;
    }

    public Memory64<byte> Data => throw new NotImplementedException();

    public DataSourceFlags Flags { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
}
