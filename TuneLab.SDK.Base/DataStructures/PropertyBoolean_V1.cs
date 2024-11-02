using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base.DataStructures;

public readonly struct PropertyBoolean_V1
{
    public static implicit operator bool(PropertyBoolean_V1 property) => property.mValue;

    public static implicit operator PropertyBoolean_V1(bool value) => new(value);

    public PropertyBoolean_V1(bool value) { mValue = value; }

    readonly bool mValue;
}
