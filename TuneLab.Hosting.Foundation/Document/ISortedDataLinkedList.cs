using System.Collections.Generic;

namespace TuneLab.Foundation;

// 有序 + Data(undo/notify) 的可变契约：挂在有序支 ISortedLinkedList<T> 下，故只有按序 Insert，无定位口。
internal interface ISortedDataLinkedList<T> : IDataObject<IEnumerable<T>>, IReadOnlyDataObject<List<T>>, IReadOnlyDataLinkedList<T>, ISortedLinkedList<T> where T : class, ILinkedNode<T>
{
    new List<T> GetInfo();
    List<T> IReadOnlyDataObject<List<T>>.GetInfo() => GetInfo();
    IEnumerable<T> IReadOnlyDataObject<IEnumerable<T>>.GetInfo() => GetInfo();
}
