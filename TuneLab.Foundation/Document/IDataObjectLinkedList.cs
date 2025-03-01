using TuneLab.Foundation.DataStructures;

namespace TuneLab.Foundation.Document;

internal interface IDataObjectLinkedList<T> : IDataLinkedList<T>, IReadOnlyDataObjectLinkedList<T> where T : class, IDataObject, ILinkedNode<T>
{

}
