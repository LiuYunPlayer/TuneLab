using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base.DataStructures;

public readonly struct PropertyValue_V1
{
    public static implicit operator PropertyValue_V1(bool value) => new(value);
    public static implicit operator PropertyValue_V1(PropertyNumber_V1 value) => new(value);
    public static implicit operator PropertyValue_V1(string value) => new(value);
    public static implicit operator PropertyValue_V1(PropertyObject_V1 value) => new(value);

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

    public PropertyValue_V1(bool value) : this((object)value) { }
    public PropertyValue_V1(PropertyNumber_V1 value) : this((object)value) { }
    public PropertyValue_V1(string value) : this((object)value) { }
    public PropertyValue_V1(PropertyObject_V1 value) : this((object)value) { }

    PropertyValue_V1(object? value = null) { mValue = value; }

    public bool IsNull() => mValue == null;
    public bool IsBool() => mValue is bool;
    public bool IsNumber() => mValue is PropertyNumber_V1;
    public bool IsString() => mValue is string;
    public bool IsObject() => mValue is PropertyObject_V1;

    public bool AsBool() => (bool)mValue!;
    public PropertyNumber_V1 AsNumber() => (PropertyNumber_V1)mValue!;
    public string AsString() => (string)mValue!;
    public PropertyObject_V1 AsObject() => (PropertyObject_V1)mValue!;

    public bool ToBool(out bool value) { if (IsBool()) { value = AsBool(); return true; } value = default; return false; }
    public bool ToNumber(out PropertyNumber_V1 value) { if (IsNumber()) { value = AsNumber(); return true; } value = default; return false; }
    public bool ToString(out string value) { if (IsString()) { value = AsString(); return true; } value = string.Empty; return false; }
    public bool ToObject(out PropertyObject_V1 value) { if (IsObject()) { value = AsObject(); return true; } value = []; return false; }

    readonly object? mValue;
}
