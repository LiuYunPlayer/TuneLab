using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Event;

namespace TuneLab.Foundation.Document;

public interface IReadOnlyDataList<out T> : IReadOnlyDataObject<IEnumerable<T>>, IReadOnlyList<T>
{
    IActionEvent<T> ItemAdded { get; }
    IActionEvent<T> ItemRemoved { get; }
    IActionEvent<T, T> ItemReplaced { get; }
}
