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

using log4net;

using Yafex.FileSystems.Base;

namespace Yafex.Fuse;

/// <summary>
/// Shared walk over any DiscUtils filesystem, for flat extraction and mount
/// trees alike. Symlink/device policy lives in the drivers: entries that
/// read as empty come out empty, entries that throw are skipped with a warn.
/// </summary>
public static class DiscTree
{
    private static readonly ILog log = LogManager.GetLogger(typeof(DiscTree));

    /// <summary>Recursive (path, content) walk for flat extractors.</summary>
    public static IEnumerable<(string Path, byte[] Content)> Flatten(DiscFileSystem fs)
    {
        return Flatten(fs, fs.Root, "");
    }

    private static IEnumerable<(string Path, byte[] Content)> Flatten(
        DiscFileSystem fs, DiscDirectoryInfo dir, string prefix)
    {
        DiscDirectoryInfo[] dirs;
        DiscFileInfo[] files;
        try
        {
            dirs = dir.GetDirectories();
            files = dir.GetFiles();
        }
        catch (Exception ex)
        {
            log.Warn($"skipping unreadable disc branch '{dir.FullName}'", ex);
            yield break;
        }
        foreach (var d in dirs)
        {
            foreach (var sub in Flatten(fs, d, prefix + d.Name + "/"))
            {
                yield return sub;
            }
        }
        foreach (var f in files)
        {
            byte[] content;
            try
            {
                using var s = f.OpenRead();
                content = new byte[s.Length];
                int off = 0;
                while (off < content.Length)
                {
                    int n = s.Read(content, off, content.Length - off);
                    if (n <= 0) throw new InvalidDataException("short disc file read");
                    off += n;
                }
            }
            catch (Exception ex)
            {
                // hostile entries (e.g. dangling symlinks): keep siblings
                log.Warn($"skipping disc entry '{f.FullName}'", ex);
                continue;
            }
            yield return (prefix + f.Name, content);
        }
    }

    /// <summary>Mount tree builder for any DiscUtils filesystem.</summary>
    public static void BuildNodes(IVfsNode vfsNode, DiscFileSystem fs)
    {
        var gate = new object();
        BuildNodes(vfsNode, fs.Root, gate);
    }

    private static void BuildNodes(IVfsNode vfsNode, DiscDirectoryInfo dirNode, object gate)
    {
        DiscDirectoryInfo[] dirs;
        DiscFileInfo[] files;
        try
        {
            dirs = dirNode.GetDirectories();
            files = dirNode.GetFiles();
        }
        catch (Exception ex)
        {
            // hostile entries (e.g. symlink variants DiscUtils cannot parse):
            // keep siblings, skip this branch
            log.Warn($"skipping unreadable disc branch '{dirNode.FullName}'", ex);
            return;
        }
        foreach (var d in dirs)
        {
            var subdir = new DiscDirNode(d.Name, Helpers.OctalLiteral(0755));
            vfsNode.AddNode(subdir);
            BuildNodes(subdir, d, gate);
        }
        foreach (var f in files)
        {
            try
            {
                vfsNode.AddNode(new DiscFileNode(f, Helpers.OctalLiteral(0666), gate));
            }
            catch (Exception ex)
            {
                // hostile entries (e.g. dangling symlinks whose target cannot
                // be resolved): keep siblings, skip the entry
                log.Warn($"skipping unreadable disc entry '{f.FullName}'", ex);
            }
        }
    }
}
