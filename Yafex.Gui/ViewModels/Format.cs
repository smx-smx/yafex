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
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Yafex.Gui.ViewModels;

/// <summary>Display formatting shared by the view models.</summary>
public static class Format
{
    private static readonly string[] Units = ["B", "KiB", "MiB", "GiB", "TiB"];

    /// <summary>`1.5 MiB`, `512 B`.</summary>
    public static string Size(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString(value < 10 ? "0.0#" : value < 100 ? "0.0" : "0", CultureInfo.InvariantCulture) + " " + Units[unit];
    }

    /// <summary>`1.5 MiB (1,572,864 bytes)`.</summary>
    public static string SizeDetailed(long bytes)
    {
        return bytes < 1024 ? Size(bytes) : $"{Size(bytes)} ({bytes.ToString("N0", CultureInfo.InvariantCulture)} bytes)";
    }

    /// <summary>`0755 (drwxr-xr-x)` from a stat mode.</summary>
    public static string Mode(int mode, bool isDirectory)
    {
        var sb = new StringBuilder(10);
        sb.Append(isDirectory ? 'd' : (mode & 0xF000) == 0xA000 ? 'l' : '-');
        const string rwx = "rwx";
        for (int i = 8; i >= 0; i--)
        {
            sb.Append((mode & (1 << i)) != 0 ? rwx[(8 - i) % 3] : '-');
        }
        return $"{Convert.ToString(mode & 0xFFF, 8).PadLeft(4, '0')} ({sb})";
    }

    /// <summary>
    /// Name matcher for the filter box: `*`/`?` globs match the whole name,
    /// plain text matches anywhere; both ignore case.
    /// </summary>
    public static Func<string, bool> NameMatcher(string pattern)
    {
        pattern = pattern.Trim();
        if (pattern.Length == 0) return _ => true;
        if (pattern.IndexOfAny(['*', '?']) < 0)
        {
            return name => name.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
        var regex = new Regex(
            "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return regex.IsMatch;
    }
}
