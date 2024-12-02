using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Foundation.Document;

public interface IDataMap<TKey, TValue> : IDataObject<IReadOnlyMap<TKey, TValue>>, IReadOnlyDataMap<TKey, TValue>, IMap<TKey, TValue> where TKey : notnull
{
    new Map<TKey, TValue> GetInfo();
    IReadOnlyMap<TKey, TValue> IReadOnlyDataObject<IReadOnlyMap<TKey, TValue>>.GetInfo() => GetInfo();
}
