namespace TuneLab.SDK.Base.Property;

public readonly struct PrimitiveValue_V1
{
    public static implicit operator PrimitiveValue_V1(PropertyBoolean_V1 value) => new(value);
    public static implicit operator PrimitiveValue_V1(PropertyNumber_V1 value) => new(value);
    public static implicit operator PrimitiveValue_V1(PropertyString_V1 value) => new(value);

    public static implicit operator PrimitiveValue_V1(bool value) => new((PropertyBoolean_V1)value);

    public static implicit operator PrimitiveValue_V1(sbyte value) => new((PropertyNumber_V1)value);
    public static implicit operator PrimitiveValue_V1(byte value) => new((PropertyNumber_V1)value);
    public static implicit operator PrimitiveValue_V1(short value) => new((PropertyNumber_V1)value);
    public static implicit operator PrimitiveValue_V1(ushort value) => new((PropertyNumber_V1)value);
    public static implicit operator PrimitiveValue_V1(int value) => new((PropertyNumber_V1)value);
    public static implicit operator PrimitiveValue_V1(uint value) => new((PropertyNumber_V1)value);
    public static implicit operator PrimitiveValue_V1(long value) => new((PropertyNumber_V1)value);
    public static implicit operator PrimitiveValue_V1(ulong value) => new((PropertyNumber_V1)value);
    public static implicit operator PrimitiveValue_V1(nint value) => new((PropertyNumber_V1)value);
    public static implicit operator PrimitiveValue_V1(nuint value) => new((PropertyNumber_V1)value);
    public static implicit operator PrimitiveValue_V1(float value) => new((PropertyNumber_V1)value);
    public static implicit operator PrimitiveValue_V1(double value) => new((PropertyNumber_V1)value);
    public static implicit operator PrimitiveValue_V1(decimal value) => new((PropertyNumber_V1)value);

    public static implicit operator PrimitiveValue_V1(string value) => new((PropertyString_V1)value);

    public static implicit operator PropertyValue_V1(PrimitiveValue_V1 value) => new(value.mValue);

    PrimitiveValue_V1(IPrimitiveValue_V1? value = null) { mValue = value; }

    readonly IPrimitiveValue_V1? mValue;
}
