﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.DataStructures;

public interface IReadOnlyOrderedMap<TKey, out TValue> : IReadOnlyMap<TKey, TValue>, IReadOnlyList<IReadOnlyKeyWithValue<TKey, TValue>> where TKey : notnull
{
    new IReadOnlyList<TKey> Keys { get; }
    new IReadOnlyList<TValue> Values { get; }
}
