using System;
using System.Collections.Generic;
using TuneLab.Base.Data;
using TuneLab.Base.Structures;
using TuneLab.Base.Science;

namespace TuneLab.Base.Utils;

public static class Extensions
{
    public static void Fill<T>(this IList<T> list, T t)
    {
        for (int i = 0; i < list.Count; i++)
        {
            list[i] = t;
        }
    }

    public static int IndexOf<T>(this IReadOnlyList<T> list, T item) where T : class
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == item)
            {
                return i;
            }
        }

        return -1;
    }

    public static bool IsEmpty<T>(this IReadOnlyCollection<T> collection)
    {
        return collection.Count == 0;
    }

    public static void Remove<T>(this ICollection<T> collection, IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            collection.Remove(item);
        }
    }

    public static T ConstFirst<T>(this IReadOnlyList<T> list)
    {
        return list[0];
    }

    public static T ConstLast<T>(this IReadOnlyList<T> list)
    {
        return list[list.Count - 1];
    }

    public static List<TInfo> ToInfo<T, TInfo>(this IEnumerable<T> list) where T : IReadOnlyDataObject<TInfo>
    {
        var infoList = new List<TInfo>();
        foreach (var item in list)
        {
            infoList.Add(item.GetInfo());
        }
        return infoList;
    }

    public static List<TInfo> ToInfo<TInfo>(this IEnumerable<IReadOnlyDataObject<TInfo>> list)
    {
        return list.ToInfo<IReadOnlyDataObject<TInfo>, TInfo>();
    }

    public static Map<TKey, TInfo> ToInfo<TKey, TInfo>(this IReadOnlyMap<TKey, IDataObject<TInfo>> map) where TKey : notnull
    {
        var infoMap = new Map<TKey, TInfo>();
        foreach (var kvp in map)
        {
            infoMap.Add(kvp.Key, kvp.Value.GetInfo());
        }
        return infoMap;
    }

    public static T ToEnum<T>(this string value, T defaultValue = default) where T : struct, Enum
    {
        return Enum.TryParse(typeof(T), value, out var result) ? (T)result : defaultValue;
    }

    public static void Resize<T>(this IList<T> list, int size) where T : new()
    {
        int currentSize = list.Count;
        if (size < currentSize)
        {
            for (int i = currentSize - 1; i >= size; i--)
            {
                list.RemoveAt(i);
            }
            return;
        }

        if (size > currentSize)
        {
            for (int i = currentSize; i < size; i++)
            {
                list.Add(new T());
            }
        }
    }
}