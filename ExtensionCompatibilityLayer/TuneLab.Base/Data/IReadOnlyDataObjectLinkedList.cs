using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;

namespace TuneLab.Base.Data;

public interface IReadOnlyDataObjectLinkedList<out T> : IReadOnlyDataLinkedList<T>, IReadOnlyDataObjectCollection<T> where T : IDataObject
{
    IMergableEvent ListModified { get; }
}
