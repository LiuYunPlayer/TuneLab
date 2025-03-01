namespace TuneLab.Base.Structures;

public interface IReadOnlyOrderedMap<TKey, out TValue> : IReadOnlyMap<TKey, TValue>, IReadOnlyList<IReadOnlyKeyWithValue<TKey, TValue>> where TKey : notnull
{
    TKey KeyAt(int index);
    TValue ValueAt(int index);
}

internal static class IReadOnlyOrderedMapExtension
{
    public static IReadOnlyKeyWithValue<TKey, TValue> At<TKey, TValue>(this IReadOnlyOrderedMap<TKey, TValue> map, int index) where TKey : notnull
    {
        return map.At(index);
    }
}
