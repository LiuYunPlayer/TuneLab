using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

public class PropertyString_V1 : IPropertyValue_V1
{
    public static implicit operator string(PropertyString_V1 property) => property.mValue ?? string.Empty;

    public static implicit operator PropertyString_V1(string value) => new(value);

    public PropertyString_V1(string value) { mValue = value; }

    public override string ToString() => mValue ?? string.Empty;

    bool IEquatable<IPropertyValue_V1>.Equals(IPropertyValue_V1? other) => other is PropertyString_V1 property && property.mValue == mValue;

    readonly string? mValue;
}
