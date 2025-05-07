using System.Diagnostics.CodeAnalysis;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Foundation.Property;

public readonly struct ReadOnlyPropertyValue
{
    public static readonly IReadOnlyPropertyValue Null = new NullPropertyValue();

    public static implicit operator ReadOnlyPropertyValue(bool value) => new((PropertyBoolean)value);

    public static implicit operator ReadOnlyPropertyValue(sbyte value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPropertyValue(byte value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPropertyValue(short value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPropertyValue(ushort value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPropertyValue(int value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPropertyValue(uint value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPropertyValue(long value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPropertyValue(ulong value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPropertyValue(nint value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPropertyValue(nuint value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPropertyValue(float value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPropertyValue(double value) => new((PropertyNumber)value);
    public static implicit operator ReadOnlyPropertyValue(decimal value) => new((PropertyNumber)value);

    public static implicit operator ReadOnlyPropertyValue(string value) => new((PropertyString)value);

    //public static bool operator ==(ReadOnlyPropertyValue left, ReadOnlyPropertyValue right) => IReadOnlyPropertyValue.Equals(left.mValue, right.mValue);
    //public static bool operator !=(ReadOnlyPropertyValue left, ReadOnlyPropertyValue right) => !IReadOnlyPropertyValue.Equals(left.mValue, right.mValue);

    public ReadOnlyPropertyValue(IReadOnlyPropertyValue? value = null) { mValue = value; }

    public IReadOnlyPropertyValue UnBoxing() => mValue ?? Null;

    public bool IsNull => mValue == null || mValue.IsNull();
    public bool IsBoolean => mValue != null && mValue.IsBoolean();
    public bool IsNumber => mValue != null && mValue.IsNumber();
    public bool IsString => mValue != null && mValue.IsString();
    public bool IsArray => mValue != null && mValue.IsArray();
    public bool IsObject => mValue != null && mValue.IsObject();

    public bool AsBoolean() => mValue == null ? false : mValue.AsBoolean();
    public double AsNumber() => mValue == null ? 0 : mValue.AsNumber();
    public string AsString() => mValue == null ? string.Empty : mValue.AsString();
    public IReadOnlyList<IReadOnlyPropertyValue> AsArray() => mValue == null ? [] : mValue.AsArray();
    public IReadOnlyMap<string, IReadOnlyPropertyValue> AsObject() => mValue == null ? [] : mValue.AsObject();

    public bool AsBoolean(bool defaultValue) => mValue == null ? defaultValue : mValue.AsBoolean(defaultValue);
    public double AsNumber(double defaultValue) => mValue == null ? defaultValue : mValue.AsNumber(defaultValue);
    public string AsString(string defaultValue) => mValue == null ? defaultValue : mValue.AsString(defaultValue);
    public IReadOnlyList<IReadOnlyPropertyValue> AsArray(IReadOnlyList<IReadOnlyPropertyValue> defaultValue) => mValue == null ? defaultValue : mValue.AsArray(defaultValue);
    public IReadOnlyMap<string, IReadOnlyPropertyValue> AsObject(IReadOnlyMap<string, IReadOnlyPropertyValue> defaultValue) => mValue == null ? defaultValue : mValue.AsObject(defaultValue);

    public bool ToBoolean([NotNullWhen(true)][MaybeNullWhen(false)] out bool value) { if (mValue == null) { value = false; return false; } return mValue.ToBoolean(out value); }
    public bool ToNumber([NotNullWhen(true)][MaybeNullWhen(false)] out double value) { if (mValue == null) { value = 0; return false; } return mValue.ToNumber(out value); }
    public bool ToString([NotNullWhen(true)][MaybeNullWhen(false)] out string value) { if (mValue == null) { value = string.Empty; return false; } return mValue.ToString(out value); }
    public bool ToArray([NotNullWhen(true)][MaybeNullWhen(false)] out IReadOnlyList<IReadOnlyPropertyValue> value) { if (mValue == null) { value = []; return false; } return mValue.ToArray(out value); }
    public bool ToObject([NotNullWhen(true)][MaybeNullWhen(false)] out IReadOnlyMap<string, IReadOnlyPropertyValue> value) { if (mValue == null) { value = []; return false; } return mValue.ToObject(out value); }

    readonly IReadOnlyPropertyValue? mValue;

    public override string ToString()
    {
        return mValue?.ToString() ?? "null";
    }

    class NullPropertyValue : IReadOnlyPropertyValue
    {
        public PropertyType Type => PropertyType.Null;

        public override string ToString()
        {
            return "null";
        }
    }
}
