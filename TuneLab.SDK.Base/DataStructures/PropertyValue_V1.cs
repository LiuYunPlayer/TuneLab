namespace TuneLab.SDK.Base.DataStructures;

public readonly struct PropertyValue_V1
{
    public static implicit operator PropertyValue_V1(PropertyBoolean_V1 value) => new(value);
    public static implicit operator PropertyValue_V1(PropertyNumber_V1 value) => new(value);
    public static implicit operator PropertyValue_V1(PropertyString_V1 value) => new(value);
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

    public PropertyValue_V1(PropertyBoolean_V1 value) : this((object)value) { }
    public PropertyValue_V1(PropertyNumber_V1 value) : this((object)value) { }
    public PropertyValue_V1(PropertyString_V1 value) : this((object)value) { }
    public PropertyValue_V1(PropertyObject_V1 value) : this((object)value) { }

    PropertyValue_V1(object? value = null) { mValue = value; }

    public bool IsNull() => mValue == null;
    public bool IsBoolean() => mValue is PropertyBoolean_V1;
    public bool IsNumber() => mValue is PropertyNumber_V1;
    public bool IsString() => mValue is PropertyString_V1;
    public bool IsObject() => mValue is PropertyObject_V1;

    public PropertyBoolean_V1 AsBoolean() => (PropertyBoolean_V1)mValue!;
    public PropertyNumber_V1 AsNumber() => (PropertyNumber_V1)mValue!;
    public PropertyString_V1 AsString() => (PropertyString_V1)mValue!;
    public PropertyObject_V1 AsObject() => (PropertyObject_V1)mValue!;

    public bool ToBoolean(out PropertyBoolean_V1 value) { if (IsBoolean()) { value = AsBoolean(); return true; } value = default; return false; }
    public bool ToNumber(out PropertyNumber_V1 value) { if (IsNumber()) { value = AsNumber(); return true; } value = default; return false; }
    public bool ToString(out PropertyString_V1 value) { if (IsString()) { value = AsString(); return true; } value = default; return false; }
    public bool ToObject(out PropertyObject_V1 value) { if (IsObject()) { value = AsObject(); return true; } value = default; return false; }

    readonly object? mValue;
}
