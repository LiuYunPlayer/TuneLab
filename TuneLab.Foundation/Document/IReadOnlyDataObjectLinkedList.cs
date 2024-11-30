using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Event;

namespace TuneLab.Foundation.Document;

public interface IReadOnlyDataObjectLinkedList<out T> : IReadOnlyDataLinkedList<T>, IReadOnlyDataObjectCollection<T> where T : IDataObject
{
    IMergableEvent ListModified { get; }
}
