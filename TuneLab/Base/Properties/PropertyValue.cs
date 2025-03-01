using System;
using System.Diagnostics.CodeAnalysis;
using TuneLab.SDK.Base;

namespace TuneLab.Base.Properties;

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

    public bool TypeIs<T>()
    {
        return mType == typeof(T);
    }

    public bool TypeEquals(PropertyValue other)
    {
        return mType == other.mType;
    }

    public bool IsInvalid()
    {
        return mValue == invalid;
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
        return mValue.ToString();
    }

    public bool Equals(PropertyValue other)
    {
        return mValue.Equals(other.mValue);
    }

    readonly static object invalid = new();
    public readonly static PropertyValue Invalid = new(invalid);

    readonly object mValue;
    readonly Type mType;

    // V1 Adapter
    public static implicit operator PropertyValue_V1(PropertyValue propertyValue)
    {
        if (propertyValue.IsInvalid())
        {
            return default;
        }
        else if (propertyValue.ToBool(out var boolValue))
        {
            return boolValue;
        }
        else if (propertyValue.ToDouble(out var doubleValue))
        {
            return doubleValue;
        }
        else if (propertyValue.ToString(out var stringValue))
        {
            return stringValue;
        }
        else if (propertyValue.ToObject(out var objectValue))
        {
            return (PropertyObject_V1)objectValue;
        }
        else
        {
            return default;
        }
    }
}
