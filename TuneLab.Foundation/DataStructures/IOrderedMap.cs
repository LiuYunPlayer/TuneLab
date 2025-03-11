using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.DataStructures;

[CollectionBuilder(typeof(OrderedMapBuilder), nameof(OrderedMapBuilder.Create))]
public interface IOrderedMap<TKey, TValue> : IMap<TKey, TValue>, IReadOnlyOrderedMap<TKey, TValue> where TKey : notnull
{
    void Insert(int index, TKey key, TValue value);
}
