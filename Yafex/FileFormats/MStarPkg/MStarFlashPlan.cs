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

namespace Yafex.FileFormats.MStarPkg;

/// <summary>One flash operation feeding a <c>sparse_write</c> target: an
/// UpgradeImage segment, raw or LZ4-compressed per the script's
/// <c>lz4 -d</c> line, decompressing to a sparse image.</summary>
public sealed class FlashSegment
{
    public long FileOffset;
    public long StoredSize;
    public bool Compressed;
    public long DecompressedSize;
}

/// <summary>All <c>sparse_write</c> operations to one target, in script
/// (flash) order. Later images overlay earlier ones; don't-care chunks
/// leave previous content in place.</summary>
public sealed class FlashGroup
{
    public string Target = "";
    public List<FlashSegment> Segments = new();
}

/// <summary>Parses the <c>filepartload</c> / <c>lz4 -d</c> / <c>sparse_write</c>
/// chains of an mboot script into ordered per-target flash groups. Only
/// adjacent, fully-matching chains are accepted; anything else is ignored,
/// so a hostile or novel script shape yields no groups (fail closed).</summary>
public static class MStarFlashPlan
{
    private sealed class Pending
    {
        public uint MemAddr;
        public long FileOffset;
        public long StoredSize;
        public bool Compressed;
        public uint DstAddr;
        public long DecompressedSize;
    }

    private static bool TryHex(string s, out uint value)
    {
        value = 0;
        try
        {
            string t = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
            value = Convert.ToUInt32(t, 16);
            return true;
        }
        catch (Exception ex) when (ex is FormatException || ex is OverflowException)
        {
            return false;
        }
    }

    public static List<FlashGroup> ParseSparseWrites(string script, long dataLength)
    {
        var groups = new List<FlashGroup>();
        var byTarget = new Dictionary<string, FlashGroup>(StringComparer.Ordinal);
        Pending? pending = null;

        void Clear() { pending = null; }

        FlashGroup ForTarget(string target)
        {
            if (!byTarget.TryGetValue(target, out var g))
            {
                g = new FlashGroup { Target = target };
                byTarget[target] = g;
                groups.Add(g);
            }
            return g;
        }

        foreach (var rawLine in script.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;
            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            if (tokens[0] == "filepartload" && tokens.Length >= 5)
            {
                pending = null;
                if (TryHex(tokens[1], out uint mem)
                    && TryHex(tokens[3], out uint off)
                    && TryHex(tokens[4], out uint size)
                    && size > 0 && (long)off + size <= dataLength)
                {
                    pending = new Pending { MemAddr = mem, FileOffset = off, StoredSize = size };
                }
                continue;
            }

            if (tokens[0] == "lz4" && tokens.Length >= 6 && tokens[1] == "-d")
            {
                if (pending == null
                    || !TryHex(tokens[2], out uint src) || src != pending.MemAddr
                    || !TryHex(tokens[3], out uint csize) || csize != pending.StoredSize
                    || !TryHex(tokens[4], out uint dst)
                    || !TryHex(tokens[5], out uint dsize) || dsize == 0)
                {
                    Clear();
                    continue;
                }
                pending.Compressed = true;
                pending.DstAddr = dst;
                pending.DecompressedSize = dsize;
                continue;
            }

            if (tokens[0] == "sparse_write" && tokens.Length >= 4 && tokens[1] == "mmc")
            {
                if (pending != null && TryHex(tokens[2], out uint addr))
                {
                    bool owner = pending.Compressed ? addr == pending.DstAddr : addr == pending.MemAddr;
                    if (owner)
                    {
                        string target = Support.PathNames.Flatten(tokens[3]);
                        if (target.Length > 0)
                        {
                            ForTarget(target).Segments.Add(new FlashSegment
                            {
                                FileOffset = pending.FileOffset,
                                StoredSize = pending.StoredSize,
                                Compressed = pending.Compressed,
                                DecompressedSize = pending.Compressed ? pending.DecompressedSize : pending.StoredSize,
                            });
                        }
                    }
                }
                Clear();
                continue;
            }

            // Any other flash-data consumer (mmc/nand/ubi writes, secure
            // stores, ...) ends the pending chain: a later sparse_write
            // must not claim a stale segment. Erases and status lines carry
            // no data and leave it intact (erase commonly precedes the
            // sparse_write of the same group).
            if (tokens[0] == "mmc" || tokens[0] == "nand" || tokens[0] == "ubi"
                || tokens[0] == "store_secure_info" || tokens[0] == "store_nuttx_config")
            {
                if (tokens.Length >= 2 && tokens[1].StartsWith("erase", StringComparison.Ordinal))
                {
                    continue;
                }
                Clear();
            }
        }
        return groups;
    }
}
