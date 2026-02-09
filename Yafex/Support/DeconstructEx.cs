using System;
using System.Collections.Generic;
using System.Linq;

namespace Yafex.Support;

public static class DeconstructEx
{
    public static void Deconstruct<T>(this IList<T> list, out T first, out IList<T> rest)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(list.Count, 1, nameof(list.Count));
        first = list[0];
        rest = list.Skip(1).ToList();
    }

    public static void Deconstruct<T>(this IList<T> list, out T first, out T second, out IList<T> rest)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(list.Count, 2, nameof(list.Count));
        first = list[0];
        second = list[1];
        rest = list.Skip(2).ToList();
    }
}
