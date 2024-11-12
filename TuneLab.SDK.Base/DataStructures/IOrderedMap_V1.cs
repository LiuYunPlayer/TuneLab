using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

public interface IOrderedMap_V1<TKey, TValue> : IMap_V1<TKey, TValue>, IReadOnlyOrderedMap_V1<TKey, TValue> where TKey : notnull
{
    void Insert(int index, TKey key, TValue value);
}

public static class IOrderedMap_V1Extensions
{
    public static bool RemoveAt<TKey, TValue>(this IOrderedMap_V1<TKey, TValue> map, int index) where TKey : notnull
    {
        return map.Remove(map.Keys[index]);
    }
}
