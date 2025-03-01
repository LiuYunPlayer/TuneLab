namespace TuneLab.Base.Structures;

public interface IReadOnlyKeyWithValue<out TKey, out TValue>
{
    TKey Key { get; }
    TValue Value { get; }
}
