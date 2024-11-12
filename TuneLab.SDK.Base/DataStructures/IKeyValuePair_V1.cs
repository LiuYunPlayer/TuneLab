using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

public interface IKeyValuePair_V1<TKey, TValue> : IReadOnlyKeyValuePair_V1<TKey, TValue>
{
    new TKey Key { get; set; }
    new TValue Value { get; set; }

    TKey IReadOnlyKeyValuePair_V1<TKey, TValue>.Key => Key;
    TValue IReadOnlyKeyValuePair_V1<TKey, TValue>.Value => Value;
}
