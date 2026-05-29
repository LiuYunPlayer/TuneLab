using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Foundation.Document;

internal interface IDataObjectLinkedList<T> : IDataLinkedList<T>, IReadOnlyDataObjectLinkedList<T> where T : class, IDataObject, ILinkedNode<T>
{

}
