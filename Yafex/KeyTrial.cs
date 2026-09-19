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

using log4net;

namespace Yafex;

/// <summary>
/// Shared key-resolution for format detectors: an explicit
/// `-o &lt;format&gt;:key=&lt;hex&gt;` option wins, then the named key-bundle
/// collection, then "no keys". Never throws for missing configuration.
/// </summary>
public static class KeyTrial
{
    private static readonly ILog log = LogManager.GetLogger(typeof(KeyTrial));

    public static List<byte[]> ResolveCandidateKeys(
        IDictionary<string, string> args,
        KeysRepository keys,
        string collectionId,
        string optionName = "key")
    {
        var candidates = new List<byte[]>();
        if (args.TryGetValue(optionName, out var hex))
        {
            try
            {
                var raw = Convert.FromHexString(hex);
                if (raw.Length is 16 or 24 or 32) candidates.Add(raw);
                else log.WarnFormat("key option must be 16/24/32 bytes of hex");
            }
            catch (FormatException)
            {
                log.Warn("key option is not valid hex");
            }
        }
        if (keys.TryGetKeyCollection(collectionId, out var entries))
        {
            foreach (var e in entries) candidates.Add(e.key);
        }
        else
        {
            log.DebugFormat("no {0} key collection available", collectionId);
        }
        return candidates;
    }
}
