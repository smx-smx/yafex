#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 *
 * Path helpers vendored from DiscUtils (Library/DiscUtils.Core/Internal/
 * Utilities.cs), copyright (c) 2008-2011 Kenneth Bell, MIT licensed.
 * Only the members the filesystem base needs; behavior unchanged.
 */
#endregion
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Yafex.FileSystems.Base.Internal;

internal static class CoreUtilities
{
    public static U[] Map<T, U>(ICollection<T> source, Func<T, U> func)
    {
        U[] result = new U[source.Count];
        int i = 0;

        foreach (T sVal in source)
        {
            result[i++] = func(sVal);
        }

        return result;
    }

    public static U[] Map<T, U>(IEnumerable<T> source, Func<T, U> func)
    {
        List<U> result = new List<U>();

        foreach (T sVal in source)
        {
            result.Add(func(sVal));
        }

        return result.ToArray();
    }

    public static string GetDirectoryFromPath(string path)
    {
        string trimmed = path.TrimEnd('\\');

        int index = trimmed.LastIndexOf('\\');
        if (index < 0)
        {
            return string.Empty; // No directory, just a file name
        }

        return trimmed.Substring(0, index);
    }

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

    public static string ResolveRelativePath(string basePath, string relativePath)
    {
        if (string.IsNullOrEmpty(basePath))
        {
            return relativePath;
        }

        // Element-wise on '\' (firmware links also use '/'): System.IO.Path
        // treats '\' as a plain char on Unix, which broke every symlink
        // resolution on Linux. Same drop-last-component rule as before.
        basePath = basePath.Replace('/', '\\');
        relativePath = relativePath.Replace('/', '\\');
        List<string> baseElements = new List<string>(
            basePath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries));
        bool rooted = basePath.StartsWith("\\", StringComparison.Ordinal);
        if (!basePath.EndsWith("\\", StringComparison.Ordinal) && baseElements.Count > 0)
        {
            baseElements.RemoveAt(baseElements.Count - 1);
        }

        foreach (string part in relativePath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }
            if (part == "..")
            {
                if (baseElements.Count > 0)
                {
                    baseElements.RemoveAt(baseElements.Count - 1);
                }
                continue;
            }
            baseElements.Add(part);
        }

        string merged = string.Join("\\", baseElements.ToArray());
        return rooted ? "\\" + merged : merged;
    }

    public static string ResolvePath(string basePath, string path)
    {
        // Unix-absolute link targets ("/bin/...") are rooted as well.
        if (path.StartsWith("/", StringComparison.Ordinal))
        {
            path = "\\" + path.Substring(1).Replace('/', '\\');
        }
        if (!path.StartsWith("\\", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveRelativePath(basePath, path);
        }
        return path;
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
