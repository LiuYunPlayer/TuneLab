namespace TuneLab.Foundation.Property;

public readonly struct ReadOnlyPrimitiveValue
{
    public static implicit operator ReadOnlyPrimitiveValue(PropertyBoolean value) => new(value);
    public static implicit operator ReadOnlyPrimitiveValue(PropertyNumber value) => new(value);
    public static implicit operator ReadOnlyPrimitiveValue(PropertyString value) => new(value);

    public static implicit operator ReadOnlyPrimitiveValue(bool value) => new((PropertyBoolean)value);

    public static implicit operator ReadOnlyPrimitiveValue(sbyte value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPrimitiveValue(byte value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPrimitiveValue(short value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPrimitiveValue(ushort value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPrimitiveValue(int value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPrimitiveValue(uint value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPrimitiveValue(long value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPrimitiveValue(ulong value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPrimitiveValue(nint value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPrimitiveValue(nuint value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPrimitiveValue(float value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPrimitiveValue(double value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPrimitiveValue(decimal value) => new((PropertyNumber)value);

    public static implicit operator ReadOnlyPrimitiveValue(string value) => new((PropertyString)value);

    public static implicit operator ReadOnlyPropertyValue(ReadOnlyPrimitiveValue value) => new(value.mValue);

    ReadOnlyPrimitiveValue(IReadOnlyPropertyValue value) { mValue = value; }

    readonly IReadOnlyPropertyValue mValue;
}
