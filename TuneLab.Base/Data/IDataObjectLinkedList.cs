using TuneLab.Base.Structures;

namespace TuneLab.Base.Data;

internal interface IDataObjectLinkedList<T> : IDataLinkedList<T>, IReadOnlyDataObjectLinkedList<T> where T : class, IDataObject, ILinkedNode<T>
{

}
