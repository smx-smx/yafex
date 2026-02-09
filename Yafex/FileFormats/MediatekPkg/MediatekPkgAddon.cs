using System;
using System.Collections.Generic;

using Yafex.Fuse;

namespace Yafex.FileFormats.MediatekPkg;

/// <summary>
/// Custom flags to represent quirks/features of MtkPkg
/// </summary>
[Flags]
public enum MtkPkgQuirks
{
    Philips = 1 << 0,
}

[Flags]
public enum MtkPkgVariant
{
    Unknown,
    // $FIXME: find better names
    Old,
    Standard,
    New
}

public class MediatekPkgAddon : IFormatAddon
{
    public FileFormat FileFormat => FileFormat.MediatekPkg;

    private readonly KeysRepository _keys;

    public MediatekPkgAddon(KeysRepository keys)
    {
        _keys = keys;
    }

    public IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new MediatekPkgDetector(_keys);
    }

    public IFormatExtractor CreateExtractor(DetectionResult result)
    {
        return new MediatekPkgExtractor((MediatekPkgContext)result.Context!, _keys);
    }

    public IVfsNode CreateVfsNode(IDataSource ds)
    {
        throw new NotImplementedException();
    }
}
