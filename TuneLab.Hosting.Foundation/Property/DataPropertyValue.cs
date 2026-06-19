using System;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

// DataPropertyValue 的存值槽：标量 PropertyValue / 嵌套子对象 / 嵌套子数组，三选一显式持有。
// （历史实现借"PropertyObject 包活 map"走私子对象引用；PropertyObject 改为构造拷入的纯值后，
//   子节点引用由本槽显式承载——撤销命令的 before/after 携带同一子树实例，undo 重挂原节点，
//   undo 栈里子树自身的嵌套命令因此不脱靶。）
// 不变量：标量槽永不含 object/array 型 PropertyValue——写入口一律经 Canonicalize 建成活子节点槽。
public readonly struct PropertySlot : IEquatable<PropertySlot>
{
    public PropertySlot(PropertyValue value) { mValue = value; }
    public PropertySlot(DataPropertyObject obj) { mObject = obj; }
    public PropertySlot(DataPropertyArray array) { mArray = array; }

    public DataPropertyObject? Object => mObject;
    public DataPropertyArray? Array => mArray;

    // 把 PropertyValue 规范成槽：object/array 型建成活子节点（标量槽永不含复合值的不变量在此保证）；标量直入。
    public static PropertySlot Canonicalize(PropertyValue value)
    {
        if (value.ToObject(out var objectValue))
        {
            var child = new DataPropertyObject();
            child.SetInfo(objectValue);
            return new PropertySlot(child);
        }
        if (value.ToArray(out var arrayValue))
        {
            var child = new DataPropertyArray();
            child.SetInfo(arrayValue);
            return new PropertySlot(child);
        }
        return new PropertySlot(value);
    }

    // 物化为纯值（子节点槽 = 子树值快照，每次调用新算）。
    public PropertyValue ToPropertyValue()
    {
        if (mObject != null) return PropertyValue.Create(mObject.GetInfo());
        if (mArray != null) return PropertyValue.Create(mArray.GetInfo());
        return mValue;
    }

    // 去重语义（供 SetInfo 判重）：子节点槽同实例即等、异实例按子树值快照深比较（值相等时去重保旧实例）；标量深相等。
    public bool Equals(PropertySlot other)
    {
        if (mObject != null || mArray != null || other.mObject != null || other.mArray != null)
            return (ReferenceEquals(mObject, other.mObject) && ReferenceEquals(mArray, other.mArray))
                || ToPropertyValue().Equals(other.ToPropertyValue());

        return mValue.Equals(other.mValue);
    }

    public override bool Equals(object? obj) => obj is PropertySlot other && Equals(other);
    public override int GetHashCode() => ToPropertyValue().GetHashCode();   // 与深相等一致（子节点槽按值快照）
    public override string? ToString() => ToPropertyValue().ToString();

    readonly PropertyValue mValue;        // 仅标量槽有效
    readonly DataPropertyObject? mObject;
    readonly DataPropertyArray? mArray;
}

public class DataPropertyValue : DataStruct<PropertySlot>
{
    public DataPropertyValue()
    {
        SetValue(new PropertySlot(PropertyValue.Null));
    }

    public DataPropertyValue(bool value)
    {
        SetValue(new PropertySlot(PropertyValue.Create(value)));
    }

    public DataPropertyValue(double value)
    {
        SetValue(new PropertySlot(PropertyValue.Create(value)));
    }

    public DataPropertyValue(string value)
    {
        SetValue(new PropertySlot(PropertyValue.Create(value)));
    }

    // 裸写时维护嵌套子节点的 attach/detach：先脱离旧槽子节点（对象/数组），落值后挂上新槽子节点。
    protected override void SetValue(PropertySlot slot)
    {
        var old = GetInfo();
        old.Object?.Detach();
        old.Array?.Detach();
        base.SetValue(slot);
        slot.Object?.Attach(this);
        slot.Array?.Attach(this);
    }
}
