using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;

namespace TuneLab.Base.Data;

internal interface IDataObjectLinkedList<T> : IDataLinkedList<T>, IReadOnlyDataObjectLinkedList<T> where T : class, IDataObject, ILinkedNode<T>
{

}
