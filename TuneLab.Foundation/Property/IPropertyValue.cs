using System.Diagnostics.CodeAnalysis;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Foundation.Property;

public interface IPropertyValue : IReadOnlyPropertyValue//, IEquatable<IPropertyValue>
{
    bool ToArray([NotNullWhen(true)][MaybeNullWhen(false)] out IList<IPropertyValue> value) { value = default; return false; }
    bool ToObject([NotNullWhen(true)][MaybeNullWhen(false)] out IMap<string, IPropertyValue> value) { value = default; return false; }
}

public static class IPropertyValueExtensions
{/*
    public static PropertyArray AsArray(this IPropertyValue propertyValue, PropertyArray defaultValue) => propertyValue.ToArray(out var value) ? value : defaultValue;
    public static PropertyObject AsObject(this IPropertyValue propertyValue, PropertyObject defaultValue) => propertyValue.ToObject(out var value) ? new(value) : defaultValue;

    public static PropertyArray AsArray(this IPropertyValue propertyValue) => propertyValue.AsArray([]);
    public static PropertyObject AsObject(this IPropertyValue propertyValue) => propertyValue.AsObject([]);*/
}