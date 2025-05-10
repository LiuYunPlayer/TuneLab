using System.Diagnostics.CodeAnalysis;

namespace TuneLab.Foundation.Property;

public readonly struct PropertyValue
{
    public static implicit operator PropertyValue(PropertyBoolean value) => new(value);
    public static implicit operator PropertyValue(PropertyNumber value) => new(value);
    public static implicit operator PropertyValue(PropertyString value) => new(value);
    public static implicit operator PropertyValue(PropertyArray value) => new(value);
    public static implicit operator PropertyValue(PropertyObject value) => new(value);

    public static implicit operator PropertyValue(bool value) => new((PropertyBoolean)value);

    public static implicit operator PropertyValue(sbyte value) => new((PropertyNumber)value);
    public static implicit operator PropertyValue(byte value) => new((PropertyNumber)value);
    public static implicit operator PropertyValue(short value) => new((PropertyNumber)value);
    public static implicit operator PropertyValue(ushort value) => new((PropertyNumber)value);
    public static implicit operator PropertyValue(int value) => new((PropertyNumber)value);
    public static implicit operator PropertyValue(uint value) => new((PropertyNumber)value);
    public static implicit operator PropertyValue(long value) => new((PropertyNumber)value);
    public static implicit operator PropertyValue(ulong value) => new((PropertyNumber)value);
    public static implicit operator PropertyValue(nint value) => new((PropertyNumber)value);
    public static implicit operator PropertyValue(nuint value) => new((PropertyNumber)value);
    public static implicit operator PropertyValue(float value) => new((PropertyNumber)value);
    public static implicit operator PropertyValue(double value) => new((PropertyNumber)value);
    public static implicit operator PropertyValue(decimal value) => new((PropertyNumber)value);

    public static implicit operator PropertyValue(string value) => new((PropertyString)value);

    //public static bool operator ==(PropertyValue left, PropertyValue right) => Equals(left.mValue, right.mValue);
    //public static bool operator !=(PropertyValue left, PropertyValue right) => !Equals(left.mValue, right.mValue);

    public PropertyValue(PropertyBoolean value) : this((IPropertyValue)value) { }
    public PropertyValue(PropertyNumber value) : this((IPropertyValue)value) { }
    public PropertyValue(PropertyString value) : this((IPropertyValue)value) { }
    public PropertyValue(PropertyArray value) : this((IPropertyValue)value) { }
    public PropertyValue(PropertyObject value) : this((IPropertyValue)value) { }

    internal PropertyValue(IPropertyValue? value = null) { mValue = value; }

    public IPropertyValue UnBox() => mValue ?? PropertyNull.Shared;

    public bool IsNull => mValue is null;
    public bool IsBoolean => mValue is PropertyBoolean;
    public bool IsNumber => mValue is PropertyNumber;
    public bool IsString => mValue is PropertyString;
    public bool IsArray => mValue is PropertyArray;
    public bool IsObject => mValue is PropertyObject;

    public PropertyBoolean AsBoolean() => (PropertyBoolean)mValue!;
    public PropertyNumber AsNumber() => (PropertyNumber)mValue!;
    public PropertyString AsString() => (PropertyString)mValue!;
    public PropertyArray AsArray() => (PropertyArray)mValue!;
    public PropertyObject AsObject() => (PropertyObject)mValue!;

    public PropertyBoolean AsBoolean(PropertyBoolean defaultValue) => mValue as PropertyBoolean ?? defaultValue;
    public PropertyNumber AsNumber(PropertyNumber defaultValue) => mValue as PropertyNumber ?? defaultValue;
    public PropertyString AsString(PropertyString defaultValue) => mValue as PropertyString ?? defaultValue;
    public PropertyArray AsArray(PropertyArray defaultValue) => mValue as PropertyArray ?? defaultValue;
    public PropertyObject AsObject(PropertyObject defaultValue) => mValue as PropertyObject ?? defaultValue;

    public bool ToBoolean([NotNullWhen(true)][MaybeNullWhen(false)] out PropertyBoolean? value) { value = mValue as PropertyBoolean; return value != null; }
    public bool ToNumber([NotNullWhen(true)][MaybeNullWhen(false)] out PropertyNumber? value) { value = mValue as PropertyNumber; return value != null; }
    public bool ToString([NotNullWhen(true)][MaybeNullWhen(false)] out PropertyString? value) { value = mValue as PropertyString; return value != null; }
    public bool ToArray([NotNullWhen(true)][MaybeNullWhen(false)] out PropertyArray? value) { value = mValue as PropertyArray; return value != null; }
    public bool ToObject([NotNullWhen(true)][MaybeNullWhen(false)] out PropertyObject? value) { value = mValue as PropertyObject; return value != null; }

    public IReadOnlyPropertyValue AsReadOnly() => mValue ?? PropertyNull.Shared;

    readonly IPropertyValue? mValue;
    /*
    public override bool Equals(object? obj)
    {
        return obj is PropertyValue other && this == other;
    }*/
    /*
    public override int GetHashCode()
    {
        return mValue?.GetHashCode() ?? 0;
    }*/

    public override string ToString()
    {
        return mValue?.ToString() ?? "null";
    }
    /*
    static bool Equals(IPropertyValue? valueA, IPropertyValue? valueB)
    {
        if (valueA == valueB)
            return true;

        if (valueA == null || valueB == null)
            return false;

        return valueA.Equals(valueB);
    }*/
}

public static class PropertyValue_V1Extensions
{
    public static bool IsNull(this PropertyValue value) => value.IsNull;
    public static bool IsBoolean(this PropertyValue value) => value.IsBoolean;
    public static bool IsNumber(this PropertyValue value) => value.IsNumber;
    public static bool IsString(this PropertyValue value) => value.IsString;
    public static bool IsArray(this PropertyValue value) => value.IsArray;
    public static bool IsObject(this PropertyValue value) => value.IsObject;
}
