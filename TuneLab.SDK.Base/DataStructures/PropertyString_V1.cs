using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base.DataStructures;

public readonly struct PropertyString_V1
{
    public static implicit operator string(PropertyString_V1 property) => property.mValue ?? string.Empty;

    public static implicit operator PropertyString_V1(string value) => new(value);

    public PropertyString_V1(string value) { mValue = value; }

    readonly string? mValue;
}
