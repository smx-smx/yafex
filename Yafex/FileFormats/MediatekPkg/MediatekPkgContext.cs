using System;

namespace Yafex.FileFormats.MediatekPkg;
public class MediatekPkgContext
{
    public MtkPkgVariant Variant { get; set; }

    public MtkPkgQuirks Flags { get; set; }
    /// <summary>
    /// Decrypted header
    /// </summary>
    public Memory<PkgHeader> Header { get; set; }
}
