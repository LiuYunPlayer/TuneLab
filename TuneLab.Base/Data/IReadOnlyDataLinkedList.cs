using TuneLab.Base.Event;
using TuneLab.Base.Structures;

namespace TuneLab.Base.Data;

public interface IReadOnlyDataLinkedList<out T> : IReadOnlyDataObject<IEnumerable<T>>, IReadOnlyLinkedList<T>
{
    IActionEvent<T> ItemAdded { get; }
    IActionEvent<T> ItemRemoved { get; }
}
