using TuneLab.SDK.Base;

namespace TuneLab.Foundation.DataStructures;

public interface IReadOnlyKeyWithValue<out TKey, out TValue> : IReadOnlyKeyValuePair_V1<TKey, TValue>
{
    new TKey Key { get; }
    new TValue Value { get; }
}
