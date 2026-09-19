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
using System.IO;
using System.Linq;

using Yafex.Metadata;

namespace Yafex;

/// <summary>
/// Single choke point for building output artifacts. Conventions (enforced here):
/// <list type="bullet">
/// <item>names are set on the BUFFER before materializing (ToDataSource preserves them);</item>
/// <item><see cref="OutputDirectoryName"/> is ABSOLUTE: Path.Combine drops the base
/// when it is rooted, so writers produce a single-level directory;</item>
/// <item>the child link is attached last, so nested extraction inherits the right base.</item>
/// </list>
/// </summary>
public static class ArtifactBuilder
{
    /// <summary>Absolute output dir for a firmware-style version subfolder.</summary>
    public static string VersionedDir(IDataSource source, string version)
    {
        var inputBase = source.GetMetadata<BaseDirectoryPath>().LastOrDefault()?.DirectoryPath;
        return inputBase != null ? Path.Combine(inputBase, version) : version;
    }

    /// <summary>Starts a streaming artifact buffer with the output file name attached.</summary>
    /// <param name="hint">
    /// Storage choice: <see cref="SpoolHint.Memory"/> keeps RAM (small artifacts),
    /// <see cref="SpoolHint.File"/> streams to <paramref name="dirName"/>/<paramref name="fileName"/>
    /// via a same-dir `.partial` sidecar + atomic rename,
    /// <see cref="SpoolHint.Auto"/> (default) spills past
    /// <see cref="ArtifactSpool.ThresholdBytes"/>.
    /// </param>
    public static IArtifactBuffer BeginBuffer(
        string dirName, string fileName, DataSourceFlags flags, SpoolHint hint = SpoolHint.Auto)
    {
        IArtifactBuffer buff = hint == SpoolHint.Memory
            ? new MemoryDataSourceBuffer(fileName, flags)
            : new SpoolBuffer(Path.Combine(dirName, fileName), fileName, flags, hint);
        buff.AddMetadata(new OutputFileName(fileName));
        return buff;
    }

    /// <summary>
    /// Materializes the buffer, attaches the (absolute) output dir + child link.
    /// </summary>
    public static IDataSource Complete(IArtifactBuffer buff, IDataSource parent, string dirName)
    {
        buff.AddMetadata(new OutputDirectoryName(dirName));
        var ds = buff.ToDataSource();
        buff.Dispose();
        ds.SetChildOf(parent);
        return ds;
    }

    /// <summary>One-shot artifact from a byte array.</summary>
    public static IDataSource FromBytes(
        IDataSource parent, string dirName, string fileName, byte[] content,
        DataSourceFlags flags = DataSourceFlags.Output, SpoolHint hint = SpoolHint.Auto)
    {
        var buff = BeginBuffer(dirName, fileName, flags, hint);
        buff.Write(content);
        return Complete(buff, parent, dirName);
    }
}
