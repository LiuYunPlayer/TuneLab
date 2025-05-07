using System.Diagnostics.CodeAnalysis;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Foundation.Property;

public interface IReadOnlyPropertyValue// : IEquatable<IReadOnlyPropertyValue>
{
    PropertyType Type { get; }

    [MemberNotNullWhen(true, nameof(Object))]
    bool IsObject => Type == PropertyType.Object;
    IReadOnlyMap<string, IReadOnlyPropertyValue>? Object => default;

    bool ToBoolean([NotNullWhen(true)][MaybeNullWhen(false)] out bool value) { value = default; return false; }
    bool ToNumber([NotNullWhen(true)][MaybeNullWhen(false)] out double value) { value = default; return false; }
    bool ToString([NotNullWhen(true)][MaybeNullWhen(false)] out string value) { value = default; return false; }
    bool ToArray([NotNullWhen(true)][MaybeNullWhen(false)] out IReadOnlyList<IReadOnlyPropertyValue> value) { value = default; return false; }
    bool ToObject([NotNullWhen(true)][MaybeNullWhen(false)] out IReadOnlyMap<string, IReadOnlyPropertyValue> value) { value = default; return false; }
    /*
    static bool Equals(IReadOnlyPropertyValue? valueA, IReadOnlyPropertyValue? valueB)
    {
        if (valueA == valueB)
            return true;

        if (valueA.Type != valueB.Type)
            return false;

        switch (valueA.Type)
        {
            case PropertyType.Null:
                return true;
            case PropertyType.Boolean:
                return valueA.ToBoolean(out var boolValueA) && valueB.ToBoolean(out var boolValueB) && boolValueA == boolValueB;
            case PropertyType.Number:
                return valueA.ToNumber(out var numberValueA) && valueB.ToNumber(out var numberValueB) && numberValueA == numberValueB;
            case PropertyType.String:
                return valueA.ToString(out var stringValueA) && valueB.ToString(out var stringValueB) && stringValueA == stringValueB;
            case PropertyType.Array:
                return valueA.ToArray(out var arrayValueA) && valueB.ToArray(out var arrayValueB) && arrayValueA.SequenceEqual(arrayValueB);
            case PropertyType.Object:
                return valueA.ToObject(out var objectValueA) && valueB.ToObject(out var objectValueB) && objectValueA.SequenceEqual(objectValueB);
            default:
                throw new InvalidOperationException();
        }
    }*/
}

public static class IReadOnlyPropertyValueExtensions
{
    public static bool IsNull(this IReadOnlyPropertyValue value) => value.Type == PropertyType.Null;
    public static bool IsBoolean(this IReadOnlyPropertyValue value) => value.Type == PropertyType.Boolean;
    public static bool IsNumber(this IReadOnlyPropertyValue value) => value.Type == PropertyType.Number;
    public static bool IsString(this IReadOnlyPropertyValue value) => value.Type == PropertyType.String;
    public static bool IsArray(this IReadOnlyPropertyValue value) => value.Type == PropertyType.Array;
    public static bool IsObject(this IReadOnlyPropertyValue value) => value.Type == PropertyType.Object;

    public static bool AsBoolean(this IReadOnlyPropertyValue propertyValue, bool defaultValue) => propertyValue.ToBoolean(out var value) ? value : defaultValue;
    public static double AsNumber(this IReadOnlyPropertyValue propertyValue, double defaultValue) => propertyValue.ToNumber(out var value) ? value : defaultValue;
    public static string AsString(this IReadOnlyPropertyValue propertyValue, string defaultValue) => propertyValue.ToString(out var value) ? value : defaultValue;
    public static IReadOnlyList<IReadOnlyPropertyValue> AsArray(this IReadOnlyPropertyValue propertyValue, IReadOnlyList<IReadOnlyPropertyValue> defaultValue) => propertyValue.ToArray(out var value) ? value : defaultValue;
    public static IReadOnlyMap<string, IReadOnlyPropertyValue> AsObject(this IReadOnlyPropertyValue propertyValue, IReadOnlyMap<string, IReadOnlyPropertyValue> defaultValue) => propertyValue.ToObject(out var value) ? value : defaultValue;

    public static bool AsBoolean(this IReadOnlyPropertyValue propertyValue) => propertyValue.AsBoolean(false);
    public static double AsNumber(this IReadOnlyPropertyValue propertyValue) => propertyValue.AsNumber(0);
    public static string AsString(this IReadOnlyPropertyValue propertyValue) => propertyValue.AsString(string.Empty);
    public static IReadOnlyList<IReadOnlyPropertyValue> AsArray(this IReadOnlyPropertyValue propertyValue) => propertyValue.AsArray([]);
    public static IReadOnlyMap<string, IReadOnlyPropertyValue> AsObject(this IReadOnlyPropertyValue propertyValue) => propertyValue.AsObject([]);
}