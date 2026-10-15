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

namespace Yafex.Support;

/// <summary>Member-name helpers for container formats.</summary>
public static class PathNames
{
    /// <summary>
    /// Flattens an archive member path into a single file name (`a/b` becomes
    /// `a_b`), using <paramref name="emptyFallback"/> when nothing is left.
    /// </summary>
    public static string Flatten(string name, string emptyFallback = "")
    {
        string flat = name.Replace('/', '_').Replace('\\', '_').Trim();
        return flat.Length == 0 ? emptyFallback : flat;
    }

    /// <summary>Last component of a `/` or `\` separated path, trimmed.</summary>
    public static string BaseName(string path)
    {
        var parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : parts[^1].Trim();
    }
}
