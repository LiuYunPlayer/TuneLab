using System;
using System.Diagnostics.CodeAnalysis;

namespace TuneLab.Foundation;

// 单一 box 的值模型。内部用「类型标签 + 字段联合」存储，避免标量装箱：
//   number/boolean 存进 mNumber（double，bool 编码为 0/1），string/object 存进 mReference（本就是引用、零额外装箱），
//   null/multiple 哨兵仅由 mType 标签表达、不占引用槽。读取标量（ToDouble/ToBool）直接取字段，无拆箱。
// 公开 ABI 与旧实现（object mValue + System.Type）完全一致：Type/To*/Is*/Equals/Create/隐式转换签名不变。
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
        return value;
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

    // 值的类型标签。
    public PropertyType Type => mType;

    public bool TypeIs<T>()
    {
        if (typeof(T) == typeof(double)) return mType == PropertyType.Number;
        if (typeof(T) == typeof(bool)) return mType == PropertyType.Boolean;
        if (typeof(T) == typeof(string)) return mType == PropertyType.String;
        if (typeof(T) == typeof(PropertyObject)) return mType == PropertyType.Object;
        return false;
    }

    public bool TypeEquals(PropertyValue other)
    {
        return mType == other.mType;
    }

    // 空值哨兵判别。null 是合法值（JSON null；未来 array 未改元素的占位）；当前模型不区分"显式 null"与"无值/无选中/缺 key"，都归此态。
    public bool IsNull()
    {
        return mType == PropertyType.Null;
    }

    // 多值哨兵判别（多选不一致的聚合态）。与 IsNull 互斥：多值不是空值。
    public bool IsMultiple()
    {
        return mType == PropertyType.Multiple;
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

    // 标量直读字段、无拆箱。
    public bool ToBool(out bool result)
    {
        if (mType == PropertyType.Boolean)
        {
            result = mNumber != 0d;
            return true;
        }

        result = false;
        return false;
    }

    public bool ToDouble(out double result)
    {
        if (mType == PropertyType.Number)
        {
            result = mNumber;
            return true;
        }

        result = 0d;
        return false;
    }

    public bool ToString([MaybeNullWhen(false)] out string result)
    {
        if (mType == PropertyType.String)
        {
            result = (string)mReference!;
            return true;
        }

        result = null;
        return false;
    }

    public bool ToObject([MaybeNullWhen(false)] out PropertyObject result)
    {
        if (mType == PropertyType.Object)
        {
            result = (PropertyObject)mReference!;
            return true;
        }

        result = null;
        return false;
    }

    public bool ToInt(out int result)
    {
        bool success = ToDouble(out var d);
        result = (int)d;
        return success;
    }

    // 泛型读取：仅匹配确切存储类型。number/boolean 分支需装箱以返回 T（罕用的泛型路径；
    // 具体类型用上面的 To* 直读字段、零拆箱）。
    public bool To<T>([MaybeNullWhen(false)] out T result)
    {
        if (typeof(T) == typeof(double))
        {
            if (mType == PropertyType.Number) { result = (T)(object)mNumber; return true; }
        }
        else if (typeof(T) == typeof(bool))
        {
            if (mType == PropertyType.Boolean) { result = (T)(object)(mNumber != 0d); return true; }
        }
        else if (typeof(T) == typeof(string))
        {
            if (mType == PropertyType.String) { result = (T)mReference!; return true; }
        }
        else if (typeof(T) == typeof(PropertyObject))
        {
            if (mType == PropertyType.Object) { result = (T)mReference!; return true; }
        }

        result = default;
        return false;
    }

    PropertyValue(double number)
    {
        mType = PropertyType.Number;
        mNumber = number;
        mReference = null;
    }

    PropertyValue(bool value)
    {
        mType = PropertyType.Boolean;
        mNumber = value ? 1d : 0d;
        mReference = null;
    }

    PropertyValue(string value)
    {
        mType = PropertyType.String;
        mNumber = 0d;
        mReference = value;
    }

    PropertyValue(PropertyObject value)
    {
        mType = PropertyType.Object;
        mNumber = 0d;
        mReference = value;
    }

    // 哨兵（Null / Multiple）：仅标签，无标量 / 引用负载。
    PropertyValue(PropertyType tag)
    {
        mType = tag;
        mNumber = 0d;
        mReference = null;
    }

    public override string? ToString()
    {
        return mType switch
        {
            PropertyType.Null => "null",
            PropertyType.Multiple => "multiple",
            PropertyType.Boolean => (mNumber != 0d).ToString(),
            PropertyType.Number => mNumber.ToString(),
            PropertyType.String => (string)mReference!,
            PropertyType.Object => mReference!.ToString(),
            _ => "null",
        };
    }

    // 深相等性：标签 + 值比较，number/boolean 比 mNumber（double.Equals，与旧 mValue.Equals 一致地令 NaN 相等），
    // string/object 走引用对象的 Equals（PropertyObject 走 map 深比较），喂 undo 去重。
    public bool Equals(PropertyValue other)
    {
        if (mType != other.mType)
            return false;

        return mType switch
        {
            PropertyType.Number or PropertyType.Boolean => mNumber.Equals(other.mNumber),
            PropertyType.String or PropertyType.Object => mReference!.Equals(other.mReference),
            _ => true,  // Null / Multiple：同标签即相等
        };
    }

    public override bool Equals(object? obj)
    {
        return obj is PropertyValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        return mType switch
        {
            PropertyType.Number or PropertyType.Boolean => mNumber.GetHashCode(),
            PropertyType.String or PropertyType.Object => mReference?.GetHashCode() ?? 0,
            _ => (int)mType,
        };
    }

    public static bool operator ==(PropertyValue left, PropertyValue right) => left.Equals(right);
    public static bool operator !=(PropertyValue left, PropertyValue right) => !left.Equals(right);

    // 空哨兵：null 值 / 无值 / 无选中（当前模型不区分）。
    public readonly static PropertyValue Null = new(PropertyType.Null);

    // 多值哨兵：多选不一致的聚合态。与 Null 并列、彼此可区分（IsMultiple vs IsNull）。
    public readonly static PropertyValue Multiple = new(PropertyType.Multiple);

    readonly PropertyType mType;
    readonly double mNumber;
    readonly object? mReference;
}
