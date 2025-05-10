using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.Property;

internal class PropertyNull : IPrimitiveValue
{
    public static readonly PropertyNull Shared = new();
    public PropertyType Type => PropertyType.Null;

    public override string ToString()
    {
        return "null";
    }

    bool IEquatable<IPrimitiveValue>.Equals(IPrimitiveValue? other)
    {
        return other == null || other.IsNull();
    }
}
