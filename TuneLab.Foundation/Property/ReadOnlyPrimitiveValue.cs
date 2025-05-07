using System.Diagnostics.CodeAnalysis;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Foundation.Property;

public readonly struct ReadOnlyPrimitiveValue : IEquatable<ReadOnlyPrimitiveValue>
{
    public static readonly IReadOnlyPrimitiveValue Null = new NullPropertyValue();

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

    public static bool operator==(ReadOnlyPrimitiveValue left, ReadOnlyPrimitiveValue right) => left.Equals(right);
    public static bool operator!=(ReadOnlyPrimitiveValue left, ReadOnlyPrimitiveValue right) => !(left == right);

    public ReadOnlyPrimitiveValue(IReadOnlyPrimitiveValue? value = null) { mValue = value; }

    public IReadOnlyPrimitiveValue UnBoxing() => mValue ?? Null;

    public bool IsNull => mValue == null || mValue.IsNull();
    public bool IsBoolean => mValue != null && mValue.IsBoolean();
    public bool IsNumber => mValue != null && mValue.IsNumber();
    public bool IsString => mValue != null && mValue.IsString();

    public bool AsBoolean() => mValue == null ? false : mValue.AsBoolean();
    public double AsNumber() => mValue == null ? 0 : mValue.AsNumber();
    public string AsString() => mValue == null ? string.Empty : mValue.AsString();

    public bool AsBoolean(bool defaultValue) => mValue == null ? defaultValue : mValue.AsBoolean(defaultValue);
    public double AsNumber(double defaultValue) => mValue == null ? defaultValue : mValue.AsNumber(defaultValue);
    public string AsString(string defaultValue) => mValue == null ? defaultValue : mValue.AsString(defaultValue);

    public bool ToBoolean([NotNullWhen(true)][MaybeNullWhen(false)] out bool value) { if (mValue == null) { value = false; return false; } return mValue.ToBoolean(out value); }
    public bool ToNumber([NotNullWhen(true)][MaybeNullWhen(false)] out double value) { if (mValue == null) { value = 0; return false; } return mValue.ToNumber(out value); }
    public bool ToString([NotNullWhen(true)][MaybeNullWhen(false)] out string value) { if (mValue == null) { value = string.Empty; return false; } return mValue.ToString(out value); }

    public override string ToString()
    {
        return mValue?.ToString() ?? "null";
    }

    readonly IReadOnlyPrimitiveValue? mValue;

    public override bool Equals(object? obj) => obj is ReadOnlyPrimitiveValue other && Equals(other);

    public override int GetHashCode()
    {
        return mValue == null ? 0 : mValue.GetHashCode();
    }

    public bool Equals(ReadOnlyPrimitiveValue other)
    {
        if (mValue == other.mValue)
            return true;

        if (mValue == null)
            return other.IsNull;

        return mValue.Equals(other.mValue);
    }

    class NullPropertyValue : IReadOnlyPrimitiveValue
    {
        public PropertyType Type => PropertyType.Null;

        public bool Equals(IReadOnlyPrimitiveValue? other)
        {
            return other == null || other.IsNull();
        }

        public override string ToString()
        {
            return "null";
        }
    }
}
