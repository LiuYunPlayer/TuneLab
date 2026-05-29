using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Foundation.Document;

public interface IReadOnlyDataLinkedList<out T> : IReadOnlyDataObject<IEnumerable<T>>, IReadOnlyLinkedList<T>
{
    IActionEvent<T> ItemAdded { get; }
    IActionEvent<T> ItemRemoved { get; }
}
