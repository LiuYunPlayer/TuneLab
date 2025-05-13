using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.Base.Structures;

namespace TuneLab.Base.Data;

public interface IReadOnlyDataLinkedList<out T> : IReadOnlyDataObject<IEnumerable<T>>, IReadOnlyLinkedList<T>
{
    IActionEvent<T> ItemAdded { get; }
    IActionEvent<T> ItemRemoved { get; }
}
