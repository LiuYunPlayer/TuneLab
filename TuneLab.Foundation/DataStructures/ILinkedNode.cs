using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.DataStructures;

public interface ILinkedNode<T> where T : class, ILinkedNode<T>
{
    T? Next { get; set; }
    T? Last { get; set; }
    ILinkedList<T>? LinkedList { get; set; }
}
