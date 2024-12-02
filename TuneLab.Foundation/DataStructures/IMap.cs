using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK.Base;

namespace TuneLab.Foundation.DataStructures;

public interface IMap<TKey, TValue> : IReadOnlyMap<TKey, TValue> where TKey : notnull
{
    new TValue this[TKey key] { get; set; }
    void Add(TKey key, TValue value);
    bool Remove(TKey key);
    void Clear();
}
