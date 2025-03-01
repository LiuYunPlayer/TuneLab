using Microsoft.Extensions.ObjectPool;
using System;
using System.Collections.Generic;

namespace TuneLab.Utils;

internal static class ObjectPoolManager
{
    public static ObjectPool<T> GetObjectPool<T>() where T : class, new()
    {
        if (!mObjectPools.TryGetValue(typeof(T), out var value))
        {
            value = new DefaultObjectPool<T>(new DefaultPooledObjectPolicy<T>());
            mObjectPools.Add(typeof(T), value);
        }

        return (ObjectPool<T>)value;
    }

    public static T Get<T>() where T : class, new()
    {
        return GetObjectPool<T>().Get();
    }

    public static void Return<T>(T t) where T : class, new()
    {
        GetObjectPool<T>().Return(t);
    }

    static Dictionary<Type, object> mObjectPools = new();
}
