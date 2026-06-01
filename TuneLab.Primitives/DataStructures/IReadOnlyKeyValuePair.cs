namespace TuneLab.Primitives.DataStructures;

public interface IReadOnlyKeyValuePair<out TKey, out TValue>
{
    TKey Key { get; }
    TValue Value { get; }
}
