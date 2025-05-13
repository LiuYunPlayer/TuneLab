using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;

namespace TuneLab.Base.Data;

internal interface IDataLinkedList<T> : IDataObject<IEnumerable<T>>, IReadOnlyDataObject<List<T>>, IReadOnlyDataLinkedList<T>, ILinkedList<T> where T : class, ILinkedNode<T>
{
    new List<T> GetInfo();
    List<T> IReadOnlyDataObject<List<T>>.GetInfo() => GetInfo();
    IEnumerable<T> IReadOnlyDataObject<IEnumerable<T>>.GetInfo() => GetInfo();
}
