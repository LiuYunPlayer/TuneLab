using TuneLab.Base.Event;
using TuneLab.Base.Structures;

namespace TuneLab.Base.Data;

public interface IReadOnlyDataMap<TKey, out TValue> : IReadOnlyDataObject<IReadOnlyMap<TKey, TValue>>, IReadOnlyMap<TKey, TValue> where TKey : notnull
{
    IActionEvent<TKey, TValue> ItemAdded { get; }
    IActionEvent<TKey, TValue> ItemRemoved { get; }
    IActionEvent<TKey, TValue, TValue> ItemReplaced { get; }
}
