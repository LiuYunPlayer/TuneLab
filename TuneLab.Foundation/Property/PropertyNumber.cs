namespace TuneLab.Foundation.Property;

public class PropertyNumber : IPropertyNumber
{
    public double Value => mValue;

    public static implicit operator sbyte(PropertyNumber property) => (sbyte)property.mValue;
    public static implicit operator byte(PropertyNumber property) => (byte)property.mValue;
    public static implicit operator short(PropertyNumber property) => (short)property.mValue;
    public static implicit operator ushort(PropertyNumber property) => (ushort)property.mValue;
    public static implicit operator int(PropertyNumber property) => (int)property.mValue;
    public static implicit operator uint(PropertyNumber property) => (uint)property.mValue;
    public static implicit operator long(PropertyNumber property) => (long)property.mValue;
    public static implicit operator ulong(PropertyNumber property) => (ulong)property.mValue;
    public static implicit operator nint(PropertyNumber property) => (nint)property.mValue;
    public static implicit operator nuint(PropertyNumber property) => (nuint)property.mValue;
    public static implicit operator float(PropertyNumber property) => (float)property.mValue;
    public static implicit operator double(PropertyNumber property) => (double)property.mValue;
    public static implicit operator decimal(PropertyNumber property) => (decimal)property.mValue;

    public static implicit operator PropertyNumber(sbyte value) => new((double)value);
    public static implicit operator PropertyNumber(byte value) => new((double)value);
    public static implicit operator PropertyNumber(short value) => new((double)value);
    public static implicit operator PropertyNumber(ushort value) => new((double)value);
    public static implicit operator PropertyNumber(int value) => new((double)value);
    public static implicit operator PropertyNumber(uint value) => new((double)value);
    public static implicit operator PropertyNumber(long value) => new((double)value);
    public static implicit operator PropertyNumber(ulong value) => new((double)value);
    public static implicit operator PropertyNumber(nint value) => new((double)value);
    public static implicit operator PropertyNumber(nuint value) => new((double)value);
    public static implicit operator PropertyNumber(float value) => new((double)value);
    public static implicit operator PropertyNumber(double value) => new((double)value);
    public static implicit operator PropertyNumber(decimal value) => new((double)value);

    public PropertyNumber(double number) { mValue = number; }

    public override string ToString() => mValue.ToString();

    bool IEquatable<IPrimitiveValue>.Equals(IPrimitiveValue? other) => other != null && other.ToNumber(out var value) && value == mValue;

    readonly double mValue;
}
