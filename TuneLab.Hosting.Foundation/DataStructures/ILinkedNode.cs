using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation;

public interface ILinkedNode<T> where T : class, ILinkedNode<T>
{
    T? Next { get; set; }
    T? Previous { get; set; }
    ILinkedList<T>? LinkedList { get; set; }
}
