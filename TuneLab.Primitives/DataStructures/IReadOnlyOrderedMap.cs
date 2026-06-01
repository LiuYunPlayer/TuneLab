using System.Collections.Generic;

namespace TuneLab.Primitives.DataStructures;

public interface IReadOnlyOrderedMap<TKey, out TValue> : IReadOnlyMap<TKey, TValue>, IReadOnlyList<IReadOnlyKeyValuePair<TKey, TValue>> where TKey : notnull
{
    // 有序版收紧（§三.11）：顺序注册表的 Keys/Values 是有索引的 IReadOnlyList。
    new IReadOnlyList<TKey> Keys { get; }
    new IReadOnlyList<TValue> Values { get; }
    TKey KeyAt(int index);
    TValue ValueAt(int index);
}
