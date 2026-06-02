using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;

namespace TuneLab.Base.Data;

public interface IDataList<T> : IDataObject<IEnumerable<T>>, IReadOnlyDataObject<List<T>>, IReadOnlyDataList<T>, IMutableList<T>
{
    new List<T> GetInfo();
    List<T> IReadOnlyDataObject<List<T>>.GetInfo() => GetInfo();
    IEnumerable<T> IReadOnlyDataObject<IEnumerable<T>>.GetInfo() => GetInfo();
}
