using System.Diagnostics.CodeAnalysis;

namespace TuneLab.SDK.Base.DataStructures;

public readonly struct PropertyValue_V1
{
    public static implicit operator PropertyValue_V1(PropertyBoolean_V1 value) => new(value);
    public static implicit operator PropertyValue_V1(PropertyNumber_V1 value) => new(value);
    public static implicit operator PropertyValue_V1(PropertyString_V1 value) => new(value);
    public static implicit operator PropertyValue_V1(PropertyArray_V1 value) => new(value);
    public static implicit operator PropertyValue_V1(PropertyObject_V1 value) => new(value);

    public static implicit operator PropertyValue_V1(bool value) => new((PropertyBoolean_V1)value);

    public static implicit operator PropertyValue_V1(sbyte value) => new((PropertyNumber_V1)value);
    public static implicit operator PropertyValue_V1(byte value) => new((PropertyNumber_V1)value);
    public static implicit operator PropertyValue_V1(short value) => new((PropertyNumber_V1)value);
    public static implicit operator PropertyValue_V1(ushort value) => new((PropertyNumber_V1)value);
    public static implicit operator PropertyValue_V1(int value) => new((PropertyNumber_V1)value);
    public static implicit operator PropertyValue_V1(uint value) => new((PropertyNumber_V1)value);
    public static implicit operator PropertyValue_V1(long value) => new((PropertyNumber_V1)value);
    public static implicit operator PropertyValue_V1(ulong value) => new((PropertyNumber_V1)value);
    public static implicit operator PropertyValue_V1(nint value) => new((PropertyNumber_V1)value);
    public static implicit operator PropertyValue_V1(nuint value) => new((PropertyNumber_V1)value);
    public static implicit operator PropertyValue_V1(float value) => new((PropertyNumber_V1)value);
    public static implicit operator PropertyValue_V1(double value) => new((PropertyNumber_V1)value);
    public static implicit operator PropertyValue_V1(decimal value) => new((PropertyNumber_V1)value);

    public static implicit operator PropertyValue_V1(string value) => new((PropertyString_V1)value);

    public PropertyValue_V1(PropertyBoolean_V1 value) : this((IPropertyValue_V1)value) { }
    public PropertyValue_V1(PropertyNumber_V1 value) : this((IPropertyValue_V1)value) { }
    public PropertyValue_V1(PropertyString_V1 value) : this((IPropertyValue_V1)value) { }
    public PropertyValue_V1(PropertyArray_V1 value) : this((IPropertyValue_V1)value) { }
    public PropertyValue_V1(PropertyObject_V1 value) : this((IPropertyValue_V1)value) { }

    PropertyValue_V1(IPropertyValue_V1? value = null) { mValue = value; }

    public bool IsNull => mValue is null;
    public bool IsBoolean => mValue is PropertyBoolean_V1;
    public bool IsNumber => mValue is PropertyNumber_V1;
    public bool IsString => mValue is PropertyString_V1;
    public bool IsArray => mValue is PropertyArray_V1;
    public bool IsObject => mValue is PropertyObject_V1;

    public PropertyBoolean_V1 AsBoolean() => (PropertyBoolean_V1)mValue!;
    public PropertyNumber_V1 AsNumber() => (PropertyNumber_V1)mValue!;
    public PropertyString_V1 AsString() => (PropertyString_V1)mValue!;
    public PropertyArray_V1 AsArray() => (PropertyArray_V1)mValue!;
    public PropertyObject_V1 AsObject() => (PropertyObject_V1)mValue!;

    public PropertyBoolean_V1 AsBoolean(PropertyBoolean_V1 defaultValue) => mValue as PropertyBoolean_V1 ?? defaultValue;
    public PropertyNumber_V1 AsNumber(PropertyNumber_V1 defaultValue) => mValue as PropertyNumber_V1 ?? defaultValue;
    public PropertyString_V1 AsString(PropertyString_V1 defaultValue) => mValue as PropertyString_V1 ?? defaultValue;
    public PropertyArray_V1 AsArray(PropertyArray_V1 defaultValue) => mValue as PropertyArray_V1 ?? defaultValue;
    public PropertyObject_V1 AsObject(PropertyObject_V1 defaultValue) => mValue as PropertyObject_V1 ?? defaultValue;

    public bool ToBoolean([NotNullWhen(true)][MaybeNullWhen(false)] out PropertyBoolean_V1? value) { value = mValue as PropertyBoolean_V1; return value != null; }
    public bool ToNumber([NotNullWhen(true)][MaybeNullWhen(false)] out PropertyNumber_V1? value) { value = mValue as PropertyNumber_V1; return value != null; }
    public bool ToString([NotNullWhen(true)][MaybeNullWhen(false)] out PropertyString_V1? value) { value = mValue as PropertyString_V1; return value != null; }
    public bool ToArray([NotNullWhen(true)][MaybeNullWhen(false)] out PropertyArray_V1? value) { value = mValue as PropertyArray_V1; return value != null; }
    public bool ToObject([NotNullWhen(true)][MaybeNullWhen(false)] out PropertyObject_V1? value) { value = mValue as PropertyObject_V1; return value != null; }

    readonly IPropertyValue_V1? mValue;
}

public static class PropertyValue_V1Extensions
{
    public static bool IsNull(this PropertyValue_V1 value) => value.IsNull;
    public static bool IsBoolean(this PropertyValue_V1 value) => value.IsBoolean;
    public static bool IsNumber(this PropertyValue_V1 value) => value.IsNumber;
    public static bool IsString(this PropertyValue_V1 value) => value.IsString;
    public static bool IsArray(this PropertyValue_V1 value) => value.IsArray;
    public static bool IsObject(this PropertyValue_V1 value) => value.IsObject;
}
