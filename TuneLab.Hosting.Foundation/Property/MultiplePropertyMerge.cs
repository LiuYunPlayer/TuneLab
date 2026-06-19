using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

// 多选编辑下把多个属性快照合成一个三态快照（驱动「该显示哪些控件 / 行 / 键」的 config 计算）。
// 对齐规则随身份模型：
//   标量——各成员同值给该值、不等给 Multiple；
//   数组——按 index 对齐，长度取最长成员，某 index 仅部分成员有 = 差异 → 该位 Multiple（缺位算差异，同对象缺键）；
//   对象——按 key 并集对齐，某 key 仅部分成员有 = 差异 → 递归（缺键的成员以「缺席」参与，触底为 Multiple）。
// 递归触底；任一层「有成员缺该位/键」即判差异。与 live 绑定侧的 MultipleDataPropertyObject / MultipleDataPropertyArray
// 用同一套对齐规则（index 对数组、key 对对象），故 config 给出的行/键数与数据外观逐位/逐键对得上。
public static class MultiplePropertyMerge
{
    public static PropertyObject MergeSnapshots(IReadOnlyList<PropertyObject> snapshots)
    {
        if (snapshots.Count == 0)
            return PropertyObject.Empty;
        if (snapshots.Count == 1)
            return snapshots[0];

        var slots = new Slot[snapshots.Count];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = Slot.Of(PropertyValue.Create(snapshots[i]));

        return Merge(slots).ToObject(out var merged) ? merged : PropertyObject.Empty;
    }

    // 一个成员在某位/键上的参与项：缺席（短于该 index / 无该 key）以 Present=false 参与，强制该节点判差异。
    readonly struct Slot
    {
        public readonly bool Present;
        public readonly PropertyValue Value;
        Slot(bool present, PropertyValue value) { Present = present; Value = value; }
        public static Slot Of(PropertyValue value) => new(true, value);
        public static readonly Slot Absent = new(false, PropertyValue.Null);
    }

    static PropertyValue Merge(IReadOnlyList<Slot> slots)
    {
        // 任一成员缺该位/键 → 差异。
        foreach (var slot in slots)
            if (!slot.Present)
                return PropertyValue.Multiple;

        var first = slots[0].Value;
        var type = first.Type;
        for (int i = 1; i < slots.Count; i++)
            if (slots[i].Value.Type != type)
                return PropertyValue.Multiple;   // 类型不一即差异

        if (type == PropertyType.Array)
            return MergeArrays(slots);
        if (type == PropertyType.Object)
            return MergeObjects(slots);

        // 标量 / null：逐成员深比较。
        for (int i = 1; i < slots.Count; i++)
            if (!slots[i].Value.Equals(first))
                return PropertyValue.Multiple;
        return first;
    }

    static PropertyValue MergeArrays(IReadOnlyList<Slot> slots)
    {
        var arrays = new PropertyArray[slots.Count];
        int maxLen = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].Value.ToArray(out var array);
            arrays[i] = array!;
            if (array!.Count > maxLen)
                maxLen = array.Count;
        }

        var result = new List<PropertyValue>(maxLen);
        var childSlots = new Slot[slots.Count];
        for (int index = 0; index < maxLen; index++)
        {
            for (int i = 0; i < arrays.Length; i++)
                childSlots[i] = index < arrays[i].Count ? Slot.Of(arrays[i][index]) : Slot.Absent;
            result.Add(Merge(childSlots));
        }
        return new PropertyArray(result);
    }

    static PropertyValue MergeObjects(IReadOnlyList<Slot> slots)
    {
        var objects = new PropertyObject[slots.Count];
        var keys = new List<string>();
        var seen = new HashSet<string>();
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].Value.ToObject(out var obj);
            objects[i] = obj!;
            foreach (var kvp in obj!.Map)
                if (seen.Add(kvp.Key))
                    keys.Add(kvp.Key);   // 键并集（保序）
        }

        var map = new Map<string, PropertyValue>();
        var childSlots = new Slot[slots.Count];
        foreach (var key in keys)
        {
            for (int i = 0; i < objects.Length; i++)
                childSlots[i] = objects[i].Map.TryGetValue(key, out var value) ? Slot.Of(value) : Slot.Absent;
            map.Add(key, Merge(childSlots));
        }
        return new PropertyObject(map);
    }
}
