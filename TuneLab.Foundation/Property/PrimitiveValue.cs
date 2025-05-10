using System.Diagnostics.CodeAnalysis;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Foundation.Property;

public readonly struct PrimitiveValue : IEquatable<PrimitiveValue>
{
    public static implicit operator PrimitiveValue(PropertyBoolean value) => new(value);
    public static implicit operator PrimitiveValue(PropertyNumber value) => new(value);
    public static implicit operator PrimitiveValue(PropertyString value) => new(value);

    public static implicit operator PrimitiveValue(bool value) => new((PropertyBoolean)value);

    public static implicit operator PrimitiveValue(sbyte value) => new((PropertyNumber)value);
    public static implicit operator PrimitiveValue(byte value) => new((PropertyNumber)value);
    public static implicit operator PrimitiveValue(short value) => new((PropertyNumber)value);
    public static implicit operator PrimitiveValue(ushort value) => new((PropertyNumber)value);
    public static implicit operator PrimitiveValue(int value) => new((PropertyNumber)value);
    public static implicit operator PrimitiveValue(uint value) => new((PropertyNumber)value);
    public static implicit operator PrimitiveValue(long value) => new((PropertyNumber)value);
    public static implicit operator PrimitiveValue(ulong value) => new((PropertyNumber)value);
    public static implicit operator PrimitiveValue(nint value) => new((PropertyNumber)value);
    public static implicit operator PrimitiveValue(nuint value) => new((PropertyNumber)value);
    public static implicit operator PrimitiveValue(float value) => new((PropertyNumber)value);
    public static implicit operator PrimitiveValue(double value) => new((PropertyNumber)value);
    public static implicit operator PrimitiveValue(decimal value) => new((PropertyNumber)value);

    public static implicit operator PrimitiveValue(string value) => new((PropertyString)value);

    public static implicit operator ReadOnlyPropertyValue(PrimitiveValue value) => new(value.mValue);

    public static bool operator==(PrimitiveValue left, PrimitiveValue right) => left.Equals(right);
    public static bool operator!=(PrimitiveValue left, PrimitiveValue right) => !(left == right);

    public PrimitiveValue(IPrimitiveValue? value = null) { mValue = value; }

    public IPrimitiveValue UnBoxing() => mValue ?? PropertyNull.Shared;

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

    readonly IPrimitiveValue? mValue;

    public override bool Equals(object? obj) => obj is PrimitiveValue other && Equals(other);

    public override int GetHashCode()
    {
        return mValue == null ? 0 : mValue.GetHashCode();
    }

    public bool Equals(PrimitiveValue other)
    {
        if (mValue == other.mValue)
            return true;

        if (mValue == null)
            return other.IsNull;

        return mValue.Equals(other.mValue);
    }
}
