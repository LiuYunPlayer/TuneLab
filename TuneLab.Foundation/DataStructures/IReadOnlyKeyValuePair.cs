namespace TuneLab.Foundation;

public interface IReadOnlyKeyValuePair<out TKey, out TValue>
{
    TKey Key { get; }
    TValue Value { get; }
}
