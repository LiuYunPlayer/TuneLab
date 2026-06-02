using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;

namespace TuneLab.Base.Data;

public interface IReadOnlyDataObjectList<out T> : IReadOnlyDataList<T>, IReadOnlyDataObjectCollection<T> where T : IDataObject
{
    IActionEvent ListModified { get; }
}
