namespace TuneLab.SDK.Base;

public interface IReadOnlyKeyValuePair_V1<out TKey, out TValue>
{
    TKey Key { get; }
    TValue Value { get; }
}
