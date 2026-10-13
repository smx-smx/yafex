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
using System.Text.RegularExpressions;

namespace Yafex.FileSystems.Fat;

/// <summary>
/// Local copies of the Core-internal path helpers the FAT driver used
/// (DiscUtils cannot expose internals here). Behavior unchanged.
/// </summary>
internal static class FatUtilities
{
    public static string GetFileFromPath(string path)
    {
        string trimmed = path.Trim('\\');

        int index = trimmed.LastIndexOf('\\');
        if (index < 0)
        {
            return trimmed; // No directory, just a file name
        }

        return trimmed.Substring(index + 1);
    }

    public static string CombinePaths(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || (b.Length > 0 && b[0] == '\\'))
        {
            return b;
        }
        if (string.IsNullOrEmpty(b))
        {
            return a;
        }
        return a.TrimEnd('\\') + '\\' + b.TrimStart('\\');
    }

    public static Regex ConvertWildcardsToRegEx(string pattern)
    {
        if (!pattern.Contains("."))
        {
            pattern += ".";
        }

        string query = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", "[^.]") + "$";
        return new Regex(query, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
