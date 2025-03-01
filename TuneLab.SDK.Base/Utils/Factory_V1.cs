using System.Reflection;

namespace TuneLab.SDK.Base;

public static class Factory_V1
{
    public static IOrderedMap_V1<TKey, TValue> CreateOrderedMap_V1<TKey, TValue>() where TKey : notnull => Create<IOrderedMap_V1<TKey, TValue>>();

    public static bool Register<T, TImpl>(Type[] parameterTypes) where T : TImpl
    {
        var constructorInfo = typeof(TImpl).GetConstructor(parameterTypes);
        if (constructorInfo is null)
            return false;

        if (mConstructorInfos.ContainsKey(typeof(T)))
            return false;

        mConstructorInfos.Add(typeof(T), constructorInfo);
        return true;
    }

    static T Create<T>(object?[]? parameters = null)
    {
        if (mConstructorInfos.TryGetValue(typeof(T), out var constructorInfo))
            return (T)constructorInfo.Invoke(parameters);
        else
            throw new InvalidOperationException($"Type {typeof(T).Name} does not have a valid constructor.");
    }

    static readonly Dictionary<Type, ConstructorInfo> mConstructorInfos = [];
}
