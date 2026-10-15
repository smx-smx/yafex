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
using System.IO;

namespace Yafex.FileFormats.Sym;

public record SymDetectionResult(int Confidence, System.Collections.Generic.List<Sym.Symbol> Symbols) : DetectionResult(Confidence);

public class SymDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < Sym.HeaderSize)
            {
                return DetectionResult.Fail;
            }
            var bytes = data.ToArray();
            if (!Sym.TryParse(bytes, out var symbols))
            {
                return DetectionResult.Fail;
            }
            return new SymDetectionResult(90, symbols);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class SymExtractor : IFormatExtractor
{
    private readonly SymDetectionResult _result;

    public SymExtractor(SymDetectionResult result)
    {
        _result = result;
    }

    public System.Collections.Generic.IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var baseName = source.Name ?? "sym";
        if (baseName.EndsWith(".sym", StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^4];
        }
        var symbols = _result.Symbols;
        yield return ArtifactBuilder.Flat(source, baseName + ".idc",
            System.Text.Encoding.ASCII.GetBytes(Sym.ToIdc(symbols)));
        yield return ArtifactBuilder.Flat(source, baseName + "_ghidra.py",
            System.Text.Encoding.ASCII.GetBytes(Sym.ToGhidra(symbols)));
    }
}
