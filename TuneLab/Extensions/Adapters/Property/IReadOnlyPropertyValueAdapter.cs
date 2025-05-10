using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using TuneLab.Extensions.Adapters.DataStructures;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;
using TuneLab.SDK.Base.DataStructures;
using TuneLab.SDK.Base.Property;

namespace TuneLab.Extensions.Adapters.Property;

internal static class IReadOnlyPropertyValueAdapter
{
    public static IReadOnlyPropertyValue_V1 ToV1(this IReadOnlyPropertyValue domain)
    {
        return new IReadOnlyPropertyValue_V1Adapter(domain);
    }

    class IReadOnlyPropertyValue_V1Adapter(IReadOnlyPropertyValue domain) : IReadOnlyPropertyValue_V1
    {
        public PropertyType_V1 Type => PropertyType_V1.Null;//domain.Type.ToV1(); FIXME: Implement Type property

        public bool ToBoolean([NotNullWhen(true)][MaybeNullWhen(false)] out bool value) => domain.ToBoolean(out value);
        public bool ToNumber([NotNullWhen(true)][MaybeNullWhen(false)] out double value) => domain.ToNumber(out value);
        public bool ToString([NotNullWhen(true)][MaybeNullWhen(false)] out string value) => domain.ToString(out value);
        public bool ToArray([NotNullWhen(true)][MaybeNullWhen(false)] out IReadOnlyList<IReadOnlyPropertyValue_V1> value) { value = default; if (!domain.ToArray(out var list)) return false; value = list.Convert(ToV1); return true; }
        public bool ToObject([NotNullWhen(true)][MaybeNullWhen(false)] out IReadOnlyMap_V1<string, IReadOnlyPropertyValue_V1> value) { value = default; if (!domain.ToObject(out var map)) return false; value = map.Convert(ToV1).ToV1(); return true; }
    }
}
