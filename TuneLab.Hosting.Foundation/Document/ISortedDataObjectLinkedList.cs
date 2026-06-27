namespace TuneLab.Foundation;

internal interface ISortedDataObjectLinkedList<T> : ISortedDataLinkedList<T>, IReadOnlyDataObjectLinkedList<T> where T : class, IDataObject, ILinkedNode<T>
{

}
