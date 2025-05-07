using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Event;

namespace TuneLab.Foundation.Document;

public interface IReadOnlyDataLinkedList<out T> : IReadOnlyDataObject<IEnumerable<T>>, IReadOnlyDataCollection<T>, IReadOnlyLinkedList<T>
{

}
