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

/// <summary>Unix timestamps from on-disk structures.</summary>
public static class UnixTime
{
    /// <summary>Last second of 9999-12-31, the end of <see cref="DateTime"/>'s range.</summary>
    private const long MaxSeconds = 253402300799L;

    /// <summary>
    /// Unix seconds as a UTC <see cref="DateTime"/>, clamped to the representable
    /// range (hostile inodes carry garbage timestamps).
    /// </summary>
    public static DateTime ToDateTime(long seconds)
    {
        return DateTime.UnixEpoch.AddSeconds(Math.Clamp(seconds, 0L, MaxSeconds));
    }

    /// <inheritdoc cref="ToDateTime(long)"/>
    public static DateTime ToDateTime(ulong seconds)
    {
        return ToDateTime(seconds > MaxSeconds ? MaxSeconds : (long)seconds);
    }
}
