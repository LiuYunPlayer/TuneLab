using System;
using TuneLab.Foundation.Property;
using TuneLab.SDK.Base.Property;

namespace TuneLab.Extensions.Adapters.Property;

internal static class PropertyTypeAdapter
{
    public static PropertyType_V1 ToV1(this PropertyType domain)
    {
        return domain switch
        {
            PropertyType.Null => PropertyType_V1.Null,
            PropertyType.Boolean => PropertyType_V1.Boolean,
            PropertyType.Number => PropertyType_V1.Number,
            PropertyType.String => PropertyType_V1.String,
            PropertyType.Array => PropertyType_V1.Array,
            PropertyType.Object => PropertyType_V1.Object,
            _ => throw new ArgumentException("Invalid PropertyType"),
        };
    }
}
