using System;
using System.Diagnostics.CodeAnalysis;

namespace TuneLab.Primitives.Property;

public readonly struct PropertyValue : IEquatable<PropertyValue>
{
    public static implicit operator PropertyValue(bool value)
    {
        return new PropertyValue(value);
    }

    public static implicit operator PropertyValue(double value)
    {
        return new PropertyValue(value);
    }

    public static implicit operator PropertyValue(string value)
    {
        return new PropertyValue(value);
    }

    public static implicit operator PropertyValue(PropertyObject value)
    {
        return new PropertyValue(value);
    }

    public static PropertyValue Create(PropertyValue value)
    {
        return new PropertyValue(value.mValue);
    }

    public static PropertyValue Create(bool value)
    {
        return new(value);
    }

    public static PropertyValue Create(double value)
    {
        return new(value);
    }

    public static PropertyValue Create(string value)
    {
        return new(value);
    }

    public static PropertyValue Create(PropertyObject value)
    {
        return new(value);
    }

    // 值的类型标签（§三.12）。
    public PropertyType Type
    {
        get
        {
            if (mValue is null || mValue is PropertyNull)
                return PropertyType.Null;
            if (mValue is bool)
                return PropertyType.Boolean;
            if (mValue is double)
                return PropertyType.Number;
            if (mValue is string)
                return PropertyType.String;
            if (mValue is PropertyObject)
                return PropertyType.Object;
            return PropertyType.Null;
        }
    }

    public bool TypeIs<T>()
    {
        return mType == typeof(T);
    }

    public bool TypeEquals(PropertyValue other)
    {
        return mType == other.mType;
    }

    // 旧 API：保留为 PropertyNull 哨兵的转发判定（待 #12 全树模型落地后清理）。
    public bool IsNull()
    {
        return mValue is null || mValue is PropertyNull;
    }

    public bool IsInvalid()
    {
        return IsNull();
    }

    public bool IsBool()
    {
        return TypeIs<bool>();
    }

    public bool IsDouble()
    {
        return TypeIs<double>();
    }

    public bool IsString()
    {
        return TypeIs<string>();
    }

    public bool IsObject()
    {
        return TypeIs<PropertyObject>();
    }

    public bool ToBool(out bool result)
    {
        return To(out result);
    }

    public bool ToDouble(out double result)
    {
        return To(out result);
    }

    public bool ToString([MaybeNullWhen(false)] out string result)
    {
        return To(out result);
    }

    public bool ToObject([MaybeNullWhen(false)] out PropertyObject result)
    {
        return To(out result);
    }

    public bool ToInt(out int result)
    {
        bool success = To<double>(out var d);
        result = (int)d;
        return success;
    }

    public bool To<T>([MaybeNullWhen(false)] out T result)
    {
        if (TypeIs<T>())
        {
            result = (T)mValue;
            return true;
        }

        result = default;
        return false;
    }

    PropertyValue(object value)
    {
        mValue = value;
        mType = mValue.GetType();
    }

    public override string? ToString()
    {
        if (mValue is null || mValue is PropertyNull)
            return "null";
        return mValue.ToString();
    }

    // 深相等性（§三.12）：类型 + 值比较，PropertyObject 走 map 深比较，喂 undo 去重。
    public bool Equals(PropertyValue other)
    {
        if (mType != other.mType)
            return false;

        if (mValue is null || other.mValue is null)
            return ReferenceEquals(mValue, other.mValue);

        return mValue.Equals(other.mValue);
    }

    public override bool Equals(object? obj)
    {
        return obj is PropertyValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        return mValue?.GetHashCode() ?? 0;
    }

    public static bool operator ==(PropertyValue left, PropertyValue right) => left.Equals(right);
    public static bool operator !=(PropertyValue left, PropertyValue right) => !left.Equals(right);

    // 空哨兵（§三.12，替代旧 Invalid 静态）。Invalid 保留为转发别名作 build-fix 安全网。
    public readonly static PropertyValue Null = new(PropertyNull.Shared);
    public readonly static PropertyValue Invalid = Null;

    readonly object mValue;
    readonly Type mType;
}
