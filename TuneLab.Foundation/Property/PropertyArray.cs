using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace TuneLab.Foundation;

// 有序值数组，与 PropertyObject 并列的纯值对象：构造时拷入自持第一层元素，此后与传入序列的任何变化无关——
// 值语义由构造保证而非调用方纪律。元素是 PropertyValue（可嵌套对象/数组）；嵌套元素在其自身构造时已拷过
// 自己的第一层（标量本就不可变），逐层各拷一层即归纳封死整树，无需深拷。
public sealed class PropertyArray : IEquatable<PropertyArray>, IReadOnlyList<PropertyValue>
{
    public readonly static PropertyArray Empty = new(Array.Empty<PropertyValue>());

    public PropertyArray(IReadOnlyList<PropertyValue> values)
    {
        var copy = new PropertyValue[values.Count];
        for (int i = 0; i < copy.Length; i++)
            copy[i] = values[i];
        mValues = copy;
    }

    public PropertyArray(IEnumerable<PropertyValue> values)
    {
        if (values is IReadOnlyList<PropertyValue> list)
        {
            var copy = new PropertyValue[list.Count];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = list[i];
            mValues = copy;
        }
        else
        {
            mValues = new List<PropertyValue>(values).ToArray();
        }
    }

    public int Count => mValues.Length;

    public PropertyValue this[int index] => mValues[index];

    // 深相等性：顺序敏感的逐元素深比较（与 PropertyObject 的键集无序比较不同），支撑 undo 去重。
    public bool Equals(PropertyArray? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (mValues.Length != other.mValues.Length)
            return false;

        for (int i = 0; i < mValues.Length; i++)
        {
            if (!mValues[i].Equals(other.mValues[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is PropertyArray other && Equals(other);
    }

    public override int GetHashCode()
    {
        int hash = 17;
        unchecked
        {
            foreach (var value in mValues)
                hash = hash * 31 + value.GetHashCode();
        }
        return hash;
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append('[');
        for (int i = 0; i < mValues.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append(mValues[i].ToString());
        }
        builder.Append(']');
        return builder.ToString();
    }

    public IEnumerator<PropertyValue> GetEnumerator()
    {
        return ((IEnumerable<PropertyValue>)mValues).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return mValues.GetEnumerator();
    }

    readonly PropertyValue[] mValues;
}
