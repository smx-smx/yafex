using System;

using Smx.SharpIO.Memory.Buffers;

namespace Yafex.FileFormats.MediatekPkg;
public class MediatekPkgContext
{
    public MtkPkgVariant Variant { get; set; }

    public MtkPkgQuirks Flags { get; set; }
    /// <summary>
    /// Decrypted header
    /// </summary>
    public Memory64<PkgHeader> Header { get; set; }
}
