using System;
using System.Collections.Generic;

using Yafex.Fuse;
using Yafex.Support;

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

public class MediatekPkgAddon : IFormatAddon<MediatekPkgDetectionResult>
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

    public IFormatExtractor CreateExtractor(MediatekPkgDetectionResult result)
    {
        return new MediatekPkgExtractor(result.Context, _keys);
    }

    public IFormatLister? CreateLister(DetectionResult result)
    {
        return result is MediatekPkgDetectionResult typed && typed.Context != null
            ? new MediatekPkgLister(typed)
            : null;
    }

    public IVfsNode CreateVfsNode(IDataSource ds)
    {
        return new YafexFile(ds, Helpers.OctalLiteral(0444));
    }

    public YafexVfs? CreateVfs(IDataSource source, DetectionResult result)
    {
        if (source is not MFile input) return null;
        if (result is not MediatekPkgDetectionResult typed) return null;
        return MtkVfs.CreateNew(input, _keys, typed);
    }
}
