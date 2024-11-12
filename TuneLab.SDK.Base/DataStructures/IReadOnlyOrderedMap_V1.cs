using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

public interface IReadOnlyOrderedMap_V1<TKey, out TValue> : IReadOnlyMap_V1<TKey, TValue>, IReadOnlyList<IReadOnlyKeyValuePair_V1<TKey, TValue>> where TKey : notnull
{
    new IReadOnlyList<TKey> Keys { get; }
    new IReadOnlyList<TValue> Values { get; }

    IReadOnlyCollection<TKey> IReadOnlyMap_V1<TKey, TValue>.Keys => Keys;
    IReadOnlyCollection<TValue> IReadOnlyMap_V1<TKey, TValue>.Values => Values;
}
