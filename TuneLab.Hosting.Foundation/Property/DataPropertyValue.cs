using System;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

// DataPropertyValue 的存值槽：标量 PropertyValue / 嵌套子对象 / 嵌套子数组，三选一显式持有。
// （历史实现借"PropertyObject 包活 map"走私子对象引用；PropertyObject 改为构造拷入的纯值后，
//   子节点引用由本槽显式承载——撤销命令的 before/after 携带同一子树实例，undo 重挂原节点，
//   undo 栈里子树自身的嵌套命令因此不脱靶。）
// 不变量：标量槽不含 object/array 型 PropertyValue——写入口一律经 Canonicalize 建成活子节点槽；唯一例外是整块槽（Whole）：
// 宿主不解读、只整值读写的字段（画布）把复合值原样存成一块不可变的值——提交 = 存引用、撤销 = 换引用、读 = 直接给出，
// 不建子节点树（大值时节点树的提交 / 读取 / 撤销历史开销是值本身的十几倍）。整块槽里没有可导航的子节点：Object / Array 导航
// 进去读到的是"缺席"，写会建出新的子节点、覆盖掉这块值——只能整值读写。工程文件写出的内容与节点树相同（ToPropertyValue）。
public readonly struct PropertySlot : IEquatable<PropertySlot>
{
    public PropertySlot(PropertyValue value) { mValue = value; }
    public PropertySlot(DataPropertyObject obj) { mObject = obj; }
    public PropertySlot(DataPropertyArray array) { mArray = array; }

    public DataPropertyObject? Object => mObject;
    public DataPropertyArray? Array => mArray;

    // 把 PropertyValue 规范成槽：object/array 型建成活子节点（标量槽永不含复合值的不变量在此保证）；标量直入。
    // 单对象的写入口都经这里，嵌套对象 / 数组经 SetInfo 递归回到这里，故 Multiple 在此一处即拦住全树（见 EnsureStorable）。
    public static PropertySlot Canonicalize(PropertyValue value)
    {
        if (value.IsMultiple())
            throw new ArgumentException(MultipleNotStorable, nameof(value));
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

    // 整块槽：复合值原样存（不拆节点树），只整值读写（见文件头）。Multiple 一样拒（内部含它也拒）。
    public static PropertySlot Whole(PropertyValue value)
    {
        EnsureStorable(value);
        return new PropertySlot(value);
    }

    // Multiple 是多选合并读出时"各对象不一致"的标记，只在数据交互中出现，绝不入存储——写入它一定是调用方写错了
    //（典型：把多选读出的当前值原样写回）。不论单选多选一律拒绝而不是当成"不改"：静默放过只会把错误藏起来。
    // 多选扇出在动手前整值深查：等到某个成员写入时才抛，各成员会停在合并通知里。
    public static void EnsureStorable(PropertyValue value)
    {
        if (value.IsMultiple())
            throw new ArgumentException(MultipleNotStorable, nameof(value));
        if (value.ToObject(out var objectValue))
        {
            foreach (var kvp in objectValue.Map)
                EnsureStorable(kvp.Value);
        }
        else if (value.ToArray(out var arrayValue))
        {
            foreach (var element in arrayValue)
                EnsureStorable(element);
        }
    }

    const string MultipleNotStorable = "Multiple marks values that differ across a multi-selection when reading; it is not a value and cannot be stored.";

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
