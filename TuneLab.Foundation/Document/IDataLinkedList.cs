﻿using TuneLab.Foundation.DataStructures;

namespace TuneLab.Foundation.Document;

internal interface IDataLinkedList<T> : IDataObject<IEnumerable<T>>, IReadOnlyDataObject<List<T>>, IReadOnlyDataLinkedList<T>, ILinkedList<T> where T : class, ILinkedNode<T>
{
    new List<T> GetInfo();
    List<T> IReadOnlyDataObject<List<T>>.GetInfo() => GetInfo();
    IEnumerable<T> IReadOnlyDataObject<IEnumerable<T>>.GetInfo() => GetInfo();
}
