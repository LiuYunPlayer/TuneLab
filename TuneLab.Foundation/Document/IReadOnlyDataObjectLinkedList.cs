using TuneLab.Foundation.Event;

namespace TuneLab.Foundation.Document;

public interface IReadOnlyDataObjectLinkedList<out T> : IReadOnlyDataLinkedList<T> where T : IDataObject
{
    IMergableEvent ListModified { get; }
}
