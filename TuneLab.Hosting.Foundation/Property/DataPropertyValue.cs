using System;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

// DataPropertyValue 的存值槽：标量 PropertyValue 或嵌套子对象引用，二选一显式持有。
// （历史实现借"PropertyObject 包活 map"走私子对象引用；PropertyObject 改为构造拷入的纯值后，
//   子对象引用由本槽显式承载——撤销命令的 before/after 携带同一子树实例，undo 重挂原对象，
//   undo 栈里子树自身的嵌套命令因此不脱靶。）
// 不变量：标量槽永不含 object 型 PropertyValue——写入口（DataPropertyObject）一律 canonicalize 成子对象槽。
public readonly struct PropertySlot : IEquatable<PropertySlot>
{
    public PropertySlot(PropertyValue value) { mValue = value; }
    public PropertySlot(DataPropertyObject obj) { mObject = obj; }

    public DataPropertyObject? Object => mObject;

    // 物化为纯值（对象槽 = 子树值快照，每次调用新算）。
    public PropertyValue ToPropertyValue() => mObject == null ? mValue : PropertyValue.Create(mObject.GetInfo());

    // 去重语义（供 SetInfo 判重）：对象槽同实例即等、异实例按子树值快照深比较（值相等时去重保旧实例）；标量深相等。
    public bool Equals(PropertySlot other)
    {
        if (mObject != null || other.mObject != null)
            return ReferenceEquals(mObject, other.mObject)
                || (mObject != null && other.mObject != null && mObject.GetInfo().Equals(other.mObject.GetInfo()));

        return mValue.Equals(other.mValue);
    }

    public override bool Equals(object? obj) => obj is PropertySlot other && Equals(other);
    public override int GetHashCode() => ToPropertyValue().GetHashCode();   // 与深相等一致（对象槽按值快照）
    public override string? ToString() => ToPropertyValue().ToString();

    readonly PropertyValue mValue;        // 仅标量槽有效
    readonly DataPropertyObject? mObject;
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

    // 裸写时维护嵌套子对象的 attach/detach：先脱离旧槽子对象，落值后挂上新槽子对象。
    protected override void SetValue(PropertySlot slot)
    {
        GetInfo().Object?.Detach();
        base.SetValue(slot);
        slot.Object?.Attach(this);
    }
}
