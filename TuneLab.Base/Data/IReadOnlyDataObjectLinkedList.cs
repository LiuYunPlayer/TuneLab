using TuneLab.Base.Event;

namespace TuneLab.Base.Data;

public interface IReadOnlyDataObjectLinkedList<out T> : IReadOnlyDataLinkedList<T>, IReadOnlyDataObjectCollection<T> where T : IDataObject
{
    IMergableEvent ListModified { get; }
}
