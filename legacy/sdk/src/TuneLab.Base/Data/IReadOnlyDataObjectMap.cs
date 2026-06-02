using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;

namespace TuneLab.Base.Data;

public interface IReadOnlyDataObjectMap<TKey, TValue> : IReadOnlyDataMap<TKey, TValue>, IReadOnlyDataObjectCollection<TValue> where TKey : notnull where TValue : IDataObject
{
    IActionEvent MapModified { get; }
}
