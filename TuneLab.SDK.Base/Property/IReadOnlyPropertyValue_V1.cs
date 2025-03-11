using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK.Base.DataStructures;

namespace TuneLab.SDK.Base.Property;

public interface IReadOnlyPropertyValue_V1
{
    PropertyType_V1 Type { get; }

    bool ToBoolean([NotNullWhen(true)][MaybeNullWhen(false)] out bool value) { value = default; return false; }
    bool ToNumber([NotNullWhen(true)][MaybeNullWhen(false)] out double value) { value = default; return false; }
    bool ToString([NotNullWhen(true)][MaybeNullWhen(false)] out string value) { value = default; return false; }
    bool ToArray([NotNullWhen(true)][MaybeNullWhen(false)] out IReadOnlyList<IReadOnlyPropertyValue_V1> value) { value = default; return false; }
    bool ToObject([NotNullWhen(true)][MaybeNullWhen(false)] out IReadOnlyMap_V1<string, IReadOnlyPropertyValue_V1> value) { value = default; return false; }
}

public static class IReadOnlyPropertyValueExtensions
{
    public static bool IsNull(this IReadOnlyPropertyValue_V1 value) => value.Type == PropertyType_V1.Null;
    public static bool IsBoolean(this IReadOnlyPropertyValue_V1 value) => value.Type == PropertyType_V1.Boolean;
    public static bool IsNumber(this IReadOnlyPropertyValue_V1 value) => value.Type == PropertyType_V1.Number;
    public static bool IsString(this IReadOnlyPropertyValue_V1 value) => value.Type == PropertyType_V1.String;
    public static bool IsArray(this IReadOnlyPropertyValue_V1 value) => value.Type == PropertyType_V1.Array;
    public static bool IsObject(this IReadOnlyPropertyValue_V1 value) => value.Type == PropertyType_V1.Object;

    public static bool AsBoolean(this IReadOnlyPropertyValue_V1 propertyValue, bool defaultValue) => propertyValue.ToBoolean(out var value) ? value : defaultValue;
    public static double AsNumber(this IReadOnlyPropertyValue_V1 propertyValue, double defaultValue) => propertyValue.ToNumber(out var value) ? value : defaultValue;
    public static string AsString(this IReadOnlyPropertyValue_V1 propertyValue, string defaultValue) => propertyValue.ToString(out var value) ? value : defaultValue;
    public static IReadOnlyList<IReadOnlyPropertyValue_V1> AsArray(this IReadOnlyPropertyValue_V1 propertyValue, IReadOnlyList<IReadOnlyPropertyValue_V1> defaultValue) => propertyValue.ToArray(out var value) ? value : defaultValue;
    public static IReadOnlyMap_V1<string, IReadOnlyPropertyValue_V1> AsObject(this IReadOnlyPropertyValue_V1 propertyValue, IReadOnlyMap_V1<string, IReadOnlyPropertyValue_V1> defaultValue) => propertyValue.ToObject(out var value) ? value : defaultValue;

    public static bool AsBoolean(this IReadOnlyPropertyValue_V1 propertyValue) => propertyValue.AsBoolean(false);
    public static double AsNumber(this IReadOnlyPropertyValue_V1 propertyValue) => propertyValue.AsNumber(0);
    public static string AsString(this IReadOnlyPropertyValue_V1 propertyValue) => propertyValue.AsString(string.Empty);
    public static IReadOnlyList<IReadOnlyPropertyValue_V1> AsArray(this IReadOnlyPropertyValue_V1 propertyValue) => propertyValue.AsArray([]);
    public static IReadOnlyMap_V1<string, IReadOnlyPropertyValue_V1> AsObject(this IReadOnlyPropertyValue_V1 propertyValue) => propertyValue.AsObject([]);
}