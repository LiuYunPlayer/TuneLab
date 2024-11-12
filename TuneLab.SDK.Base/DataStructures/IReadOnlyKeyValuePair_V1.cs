using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

public interface IReadOnlyKeyValuePair_V1<out TKey, out TValue>
{
    TKey Key { get; }
    TValue Value { get; }
}
