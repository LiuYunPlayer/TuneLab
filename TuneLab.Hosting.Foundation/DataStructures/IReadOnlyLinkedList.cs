using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation;

public interface IReadOnlyLinkedList<out T> : IReadOnlyCollection<T>
{
    T? First { get; }
    T? Last { get; }
    IEnumerator<T> Inverse();
}
