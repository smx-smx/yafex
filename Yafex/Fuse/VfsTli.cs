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
using System.Linq;

using Yafex.FileFormats.Tar;
using Yafex.FileFormats.Tli;

namespace Yafex.Fuse;

/// <summary>
/// Mount expander for TLI firmware update containers: exposes one file per
/// tar-archived firmware image, chaining each into the nested-format
/// expander on access.
/// </summary>
public sealed class TliExpander : IContentExpander
{
    public string Name => "tli";

    public bool Matches(IVirtualFile file)
    {
        try
        {
            if (file.Length < 512)
            {
                return false;
            }
            var buf = file.ReadHead(256L * 1024);
            long scanEnd = Math.Min(buf.Length - 12, 64L * 1024);
            for (long at = 0; at <= scanEnd; at++)
            {
                if (buf[at] != TliParser.TableId)
                {
                    continue;
                }
                if (!TliParser.TryParseHeader(buf, at, out int headerLength))
                {
                    continue;
                }
                if (!TliParser.TryFindTar(buf, at + headerLength + 3, out long tarStart))
                {
                    continue;
                }
                try
                {
                    using var ms = new MemoryStream(buf, (int)tarStart, buf.Length - (int)tarStart, false);
                    int members = 0;
                    foreach (var _ in new YafexTarReader(ms).Entries)
                    {
                        members++;
                    }
                    return members > 0;
                }
                catch (Exception)
                {
                    continue;
                }
            }
            return false;
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
            byte[] image = file.ReadAvailable();
            long tarStart = -1;
            int found = 0;
            for (long at = 0; at + 12 <= image.Length && found < 64; at++)
            {
                if (image[at] != TliParser.TableId)
                {
                    continue;
                }
                found++;
                if (!TliParser.TryParseHeader(image, at, out int headerLength))
                {
                    continue;
                }
                if (!TliParser.TryFindTar(image, at + headerLength + 3, out long candidate))
                {
                    continue;
                }
                try
                {
                    using var probe = new MemoryStream(image, (int)candidate, image.Length - (int)candidate, false);
                    int members = 0;
                    foreach (var _ in new YafexTarReader(probe).Entries)
                    {
                        members++;
                    }
                    if (members == 0)
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    continue;
                }
                tarStart = candidate;
                break;
            }
            if (tarStart < 0)
            {
                throw new InvalidOperationException("no TLI image entries found");
            }
            var nodes = new List<IVfsNode>();
            using var stream = new MemoryStream(image, (int)tarStart, image.Length - (int)tarStart, false);
            var tar = new YafexTarReader(stream);
            foreach (var e in tar.Entries)
            {
                var slice = new SliceVirtualFile(TliParser.SafeName(e.Name), file, tarStart + e.Offset, e.Length);
                VfsBuilder.AddWithNested(nodes, slice, keepAlive);
            }
            return nodes;
        });
    }
}
