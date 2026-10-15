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

public class MediatekPkgAddon : FormatAddon<MediatekPkgDetectionResult>
{
    public override FileFormat FileFormat => FileFormat.MediatekPkg;

    private readonly KeysRepository _keys;

    public MediatekPkgAddon(KeysRepository keys)
    {
        _keys = keys;
    }

    public override IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new MediatekPkgDetector(_keys);
    }

    protected override bool IsUsable(MediatekPkgDetectionResult result) => result.Context != null;

    protected override IFormatExtractor CreateExtractor(MediatekPkgDetectionResult result)
    {
        return new MediatekPkgExtractor(result.Context, _keys);
    }

    protected override IFormatLister? CreateLister(MediatekPkgDetectionResult typed)
    {
        return new MediatekPkgLister(typed);
    }

    public override YafexVfs? CreateVfs(IDataSource input, DetectionResult result)
    {
        if (result is not MediatekPkgDetectionResult typed) return null;
        return MtkVfs.CreateNew(input, _keys, typed);
    }
}
