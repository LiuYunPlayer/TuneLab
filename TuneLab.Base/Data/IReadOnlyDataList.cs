using TuneLab.Base.Event;

namespace TuneLab.Base.Data;

public interface IReadOnlyDataList<out T> : IReadOnlyDataObject<IEnumerable<T>>, IReadOnlyList<T>
{
    IActionEvent<T> ItemAdded { get; }
    IActionEvent<T> ItemRemoved { get; }
    IActionEvent<T, T> ItemReplaced { get; }
}
