using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Base.Structures;

public interface IReadOnlyLinkedList<out T> : IReadOnlyCollection<T>
{
    T? Begin { get; }
    T? End { get; }
    IEnumerator<T> Inverse();
}
