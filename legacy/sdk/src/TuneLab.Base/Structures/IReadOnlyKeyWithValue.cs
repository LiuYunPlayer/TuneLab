using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Base.Structures;

public interface IReadOnlyKeyWithValue<out TKey, out TValue>
{
    TKey Key { get; }
    TValue Value { get; }
}
