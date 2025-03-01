using System;
using System.Collections.Generic;

namespace TuneLab.Extensions.Format.Adapters;

internal static class FormatAdapterExtensions
{
    public static TMap Convert<TMap, TKey, TSource, TResult>(this IReadOnlyDictionary<TKey, TSource> dictionary, Func<TSource, TResult> converter) where TMap : IDictionary<TKey, TResult>, new()
    {
        TMap map = [];
        foreach (var item in dictionary)
        {
            map.Add(item.Key, converter(item.Value));
        }
        return map;
    }
}
