using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;

namespace TuneLab.Base.Data;

public interface IDataMap<TKey, TValue> : IDataObject<IReadOnlyMap<TKey, TValue>>, IReadOnlyDataMap<TKey, TValue>, IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue> where TKey : notnull
{
    new Map<TKey, TValue> GetInfo();
    IReadOnlyMap<TKey, TValue> IReadOnlyDataObject<IReadOnlyMap<TKey, TValue>>.GetInfo() => GetInfo();
}
